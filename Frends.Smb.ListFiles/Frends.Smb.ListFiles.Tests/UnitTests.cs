using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.ListFiles.Definitions;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Frends.Smb.ListFiles.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g., WSL2).
// They will not run on Windows natively because the OS reserves SMB port 445.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
[TestFixture]
public class UnitTests
{
    private const string ServerName = "127.0.0.1";
    private const string ShareName = "test-share";
    private const string Password = "pass";
    private const string User = @"WORKGROUP\user";

    private static readonly string TestDirPath =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "test");

    private Input input;
    private Connection connection;
    private Options options;
    private IContainer sambaContainer;

    private Dictionary<string, FileItem> FileItems { get; set; } = [];

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        Directory.CreateDirectory(TestDirPath);

        // Create directory structure and files under TestDirPath
        var dir = Path.Combine(TestDirPath, "dir");
        var sub = Path.Combine(dir, "sub");
        var inner = Path.Combine(sub, "inner");
        var empty = Path.Combine(dir, "empty");

        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(inner);
        Directory.CreateDirectory(empty);

        // Create files
        var path = Path.Combine(TestDirPath, "shareRoot.txt");
        await File.WriteAllTextAsync(path, "root txt content");
        var fi = new FileInfo(path);
        FileItems.Add(
            "shareRoot.txt",
            new FileItem
            {
                Name = "shareRoot.txt",
                Path = "shareRoot.txt",
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        path = Path.Combine(dir, "root.txt");
        await File.WriteAllTextAsync(path, "root txt content");
        fi = new FileInfo(path);
        FileItems.Add(
            "root.txt",
            new FileItem
            {
                Name = "root.txt",
                Path = Path.Combine("dir", "root.txt"),
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        path = Path.Combine(dir, "root.log");
        await File.WriteAllTextAsync(path, "root log content");
        fi = new FileInfo(path);
        FileItems.Add(
            "root.log",
            new FileItem
            {
                Name = "root.log",
                Path = Path.Combine("dir", "root.log"),
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        path = Path.Combine(sub, "a.txt");
        await File.WriteAllTextAsync(path, "a txt content");
        fi = new FileInfo(path);
        FileItems.Add(
            "a.txt",
            new FileItem
            {
                Name = "a.txt",
                Path = Path.Combine("dir", "sub", "a.txt"),
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        path = Path.Combine(sub, "b.log");
        await File.WriteAllTextAsync(path, "b log content");
        fi = new FileInfo(path);
        FileItems.Add(
            "b.log",
            new FileItem
            {
                Name = "b.log",
                Path = Path.Combine("dir", "sub", "b.log"),
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        path = Path.Combine(inner, "c.txt");
        await File.WriteAllTextAsync(path, "c txt content");
        fi = new FileInfo(path);
        FileItems.Add(
            "c.txt",
            new FileItem
            {
                Name = "c.txt",
                Path = Path.Combine("dir", "sub", "inner", "c.txt"),
                CreationTime = fi.CreationTimeUtc,
                ModificationTime = fi.LastWriteTimeUtc,
                SizeInMegabyte = (int)Math.Round(fi.Length / (1024.0 * 1024.0)),
            });

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-server-{Guid.NewGuid()}")
            .WithBindMount(TestDirPath, "/share")
            .WithPortBinding(445, 445)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(445))
            .WithCommand(
                "-n",
                "-u",
                "user;pass",
                "-s",
                "test-share;/share;no;no;no;user,root",
                "-w",
                "WORKGROUP")
            .Build();

        await sambaContainer.StartAsync();
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (sambaContainer != null)
        {
            await sambaContainer.DisposeAsync();
        }

        if (Directory.Exists(TestDirPath))
            Directory.Delete(TestDirPath, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection { Server = ServerName, Share = ShareName, Username = User, Password = Password };

        options = new Options
        {
            ThrowErrorOnFailure = false,
            ErrorMessageOnFailure = string.Empty,
            Pattern = string.Empty,
            SearchRecursively = false,
        };

        input = new Input { Directory = "dir" };
    }

    [Test]
    public void Should_Work_With_Both_Slashes()
    {
        input.Directory = "/";
        var expected = new[] { FileItems["shareRoot.txt"] };

        var result1 = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result1.Success, Is.True, result1.Error?.Message);
        AssertAreEqual(expected, result1.Files);

        input.Directory = @"\";
        var result2 = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result2.Success, Is.True, result2.Error?.Message);
        AssertAreEqual(expected, result2.Files);
    }

    [Test]
    public void Should_List_Root_Files_When_Not_Recursive()
    {
        input.Directory = "/";
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { FileItems["shareRoot.txt"] };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_List_Top_Level_Files_When_Not_Recursive()
    {
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { FileItems["root.txt"], FileItems["root.log"] };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_List_All_Files_When_Recursive()
    {
        options.SearchRecursively = true;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[]
        {
            FileItems["root.txt"], FileItems["root.log"], FileItems["a.txt"], FileItems["b.log"],
            FileItems["c.txt"],
        };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_Filter_By_FileName_Regex_NonRecursive()
    {
        options.Pattern = "^.*\\.txt$"; // only .txt files
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { FileItems["root.txt"] };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_Filter_By_Path_Regex_Recursive()
    {
        options.SearchRecursively = true;
        options.Pattern = "/inner/";
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { FileItems["c.txt"] };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_Filter_By_Wildcard_Recursive()
    {
        options.SearchRecursively = true;
        options.Pattern = "*.txt";
        options.UseWildcards = true;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { FileItems["a.txt"], FileItems["root.txt"], FileItems["c.txt"] };
        AssertAreEqual(expected, result.Files);
    }

    [Test]
    public void Should_Return_Error_When_Server_Is_Empty()
    {
        connection.Server = string.Empty;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        StringAssert.Contains("Server cannot be empty", result.Error.Message);
    }

    [Test]
    public void Should_Return_Error_When_Share_Is_Empty()
    {
        connection.Share = string.Empty;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        StringAssert.Contains("Share cannot be empty", result.Error.Message);
    }

    [Test]
    public void Should_Return_Error_When_Directory_Is_Empty()
    {
        input.Directory = string.Empty;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        StringAssert.Contains("Directory cannot be empty", result.Error.Message);
    }

    [Test]
    public void Should_Return_Error_When_Directory_Is_Unc_Path()
    {
        input.Directory = @"\\server\share\path";
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        StringAssert.Contains("Path should be relative to the share", result.Error.Message);
    }

    [Test]
    public void Should_Return_Error_When_Username_Format_Is_Invalid()
    {
        connection.Username = "user-only"; // missing domain
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        StringAssert.Contains("UserName field must be of format", result.Error.Message);
    }

    private static void AssertAreEqual(FileItem[] expected, FileItem[] actual)
    {
        foreach (var expectedFile in expected)
        {
            var actualFile = actual.FirstOrDefault(x => x.Name == expectedFile.Name);

            Assert.That(actualFile, Is.Not.Null, $"File '{expectedFile.Name}' was expected but not found in result.");
            Assert.That(
                actualFile,
                Is.EqualTo(expectedFile).Using<FileItem>((a, b) =>
                    a.Name == b.Name &&
                    a.SizeInMegabyte == b.SizeInMegabyte &&
                    a.CreationTime == b.CreationTime &&
                    a.ModificationTime == b.ModificationTime
                        ? 0
                        : -1),
                @$"File '{expectedFile.Name}' does not match expected metadata.
                Name - Expected: {expectedFile.Name},  Actual: {actualFile.Name}
                Size - Expected: {expectedFile.SizeInMegabyte},  Actual: {actualFile.SizeInMegabyte}
                CreationTime - Expected: {expectedFile.CreationTime},  Actual: {actualFile.CreationTime}
                ModificationTime - Expected: {expectedFile.ModificationTime},  Actual: {actualFile.ModificationTime}");
        }
    }
}
