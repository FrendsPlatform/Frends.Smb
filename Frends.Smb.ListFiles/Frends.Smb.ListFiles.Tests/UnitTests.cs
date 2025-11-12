using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.ListFiles.Definitions;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Frends.Smb.ListFiles.Tests;

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
        await File.WriteAllTextAsync(Path.Combine(TestDirPath, "shareRoot.txt"), "root txt content");
        await File.WriteAllTextAsync(Path.Combine(dir, "root.txt"), "root txt content");
        await File.WriteAllTextAsync(Path.Combine(dir, "root.log"), "root log content");
        await File.WriteAllTextAsync(Path.Combine(sub, "a.txt"), "a txt content");
        await File.WriteAllTextAsync(Path.Combine(sub, "b.log"), "b log content");
        await File.WriteAllTextAsync(Path.Combine(inner, "c.txt"), "c txt content");

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
        var result1 = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result1.Success, Is.True, result1.Error?.Message);
        input.Directory = @"\";
        var result2 = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result2.Success, Is.True, result2.Error?.Message);

        var expected = new[] { "shareRoot.txt" };
        CollectionAssert.AreEquivalent(expected, result1.Files);
        CollectionAssert.AreEquivalent(expected, result2.Files);
    }

    [Test]
    public void Should_List_Root_Files_When_Not_Recursive()
    {
        input.Directory = "/";
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { "shareRoot.txt" };
        CollectionAssert.AreEquivalent(expected, result.Files);
    }

    [Test]
    public void Should_List_Top_Level_Files_When_Not_Recursive()
    {
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { "dir/root.txt", "dir/root.log" };
        CollectionAssert.AreEquivalent(expected, result.Files);
    }

    [Test]
    public void Should_List_All_Files_When_Recursive()
    {
        options.SearchRecursively = true;
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[]
        {
            "dir/root.txt", "dir/root.log", "dir/sub/a.txt", "dir/sub/b.log", "dir/sub/inner/c.txt",
        };
        CollectionAssert.AreEquivalent(expected, result.Files);
    }

    [Test]
    public void Should_Filter_By_FileName_Regex_NonRecursive()
    {
        options.Pattern = "^.*\\.txt$"; // only .txt files
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { "dir/root.txt" };
        CollectionAssert.AreEquivalent(expected, result.Files);
    }

    [Test]
    public void Should_Filter_By_Path_Regex_Recursive()
    {
        options.SearchRecursively = true;
        options.Pattern = "/inner/";
        var result = Smb.ListFiles(input, connection, options, CancellationToken.None);
        Assert.That(result.Success, Is.True, result.Error?.Message);
        var expected = new[] { "dir/sub/inner/c.txt" };
        CollectionAssert.AreEquivalent(expected, result.Files);
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
}
