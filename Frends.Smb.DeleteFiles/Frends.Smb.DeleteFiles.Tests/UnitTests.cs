using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.DeleteFiles.Definitions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Frends.Smb.DeleteFiles.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g. WSL2).
// They will not run on Windows natively because SMB port 445 is reserved by the OS.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
[TestFixture]
public class DeleteFilesTests
{
    private readonly string serverName = "127.0.0.1";
    private readonly string shareName = "testshare";
    private readonly string userName = "WORKGROUP\\testuser";
    private readonly string password = "testpass";

    private Input input;
    private Connection connection;
    private Options options;
    private IContainer sambaContainer;
    private string testFilesPath;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "test-files-delete");
        Directory.CreateDirectory(testFilesPath);

        Directory.CreateDirectory(Path.Combine(testFilesPath, "pattern-test"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "subdir-test"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "subdir-test", "nested"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "dirtest"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "dirtest", "nested"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "deep"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "deep", "inner"));

        File.WriteAllText(Path.Combine(testFilesPath, "rootfile.txt"), "root");

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-delete-{Guid.NewGuid()}")
            .WithBindMount(testFilesPath, "/share")
            .WithPortBinding(445, 445)
            .WithCommand(
                "-u",
                "testuser;testpass",
                "-s",
                "testshare;/share;yes;no;no;testuser",
                "-w",
                "WORKGROUP")
            .Build();

        await sambaContainer.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (sambaContainer != null)
            await sambaContainer.DisposeAsync();
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            Server = serverName,
            Share = shareName,
            UserName = userName,
            Password = password,
        };

        options = new Options
        {
            ThrowErrorOnFailure = true,
            ErrorMessageOnFailure = string.Empty,
        };
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var file in Directory.GetFiles(testFilesPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
            }
        }

        if (!File.Exists(Path.Combine(testFilesPath, "rootfile.txt")))
            File.WriteAllText(Path.Combine(testFilesPath, "rootfile.txt"), "root");
    }

    [Test]
    public async Task DeleteFiles_DirectFilePath_Success()
    {
        string fileName = "single-target.txt";
        string filePath = Path.Combine(testFilesPath, fileName);
        File.WriteAllText(filePath, "Delete me!");

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input
        {
            Path = fileName,
            Pattern = null,
        };

        options.ThrowErrorOnFailure = true;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True, "File deletion should succeed");
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1), "Exactly one file should be deleted");
        Assert.That(result.FilesDeleted.Single().Name, Is.EqualTo(fileName), "Deleted file should match expected name");

        Assert.That(File.Exists(filePath), Is.False, "The file should no longer exist on the host");
    }

    [Test]
    public async Task DeleteFiles_PatternMatch_SuccessfullyDeletesMatchingFiles()
    {
        string dir = Path.Combine(testFilesPath, "pattern-test");
        string file1 = Path.Combine(dir, "match1.txt");
        string file2 = Path.Combine(dir, "match2.txt");
        string file3 = Path.Combine(dir, "ignore.log");

        File.WriteAllText(file1, "to delete");
        File.WriteAllText(file2, "to delete");
        File.WriteAllText(file3, "keep");

        input = new Input
        {
            Path = "pattern-test",
            Pattern = "*.txt",
        };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(2));
        Assert.That(result.FilesDeleted.Any(f => f.Name == "match1.txt"), Is.True);
        Assert.That(result.FilesDeleted.Any(f => f.Name == "match2.txt"), Is.True);
    }

    [Test]
    public async Task DeleteFiles_DoesNotTouchSubdirectories()
    {
        string rootFile = Path.Combine(testFilesPath, "subdir-test", "root.txt");
        string nestedFile = Path.Combine(testFilesPath, "subdir-test", "nested", "inner.txt");

        await File.WriteAllTextAsync(rootFile, "delete me");
        await File.WriteAllTextAsync(nestedFile, "keep me");

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input
        {
            Path = "subdir-test",
            Pattern = "*.txt",
        };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(rootFile), Is.False, "Root file should be deleted");
        Assert.That(File.Exists(nestedFile), Is.True, "Files in subdirectories should not be deleted");
    }

    [Test]
    public async Task DeleteFiles_DoesNotDeleteDirectories()
    {
        string file1 = Path.Combine(testFilesPath, "dirtest", "file1.txt");
        await File.WriteAllTextAsync(file1, "delete me");

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input
        {
            Path = "dirtest",
            Pattern = "*",
        };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "dirtest", "nested")), Is.True, "Directories should remain intact");
    }

    [Test]
    public void DeleteFiles_InvalidUsernameFormat_ThrowsArgumentException()
    {
        connection.UserName = "invalidUser";
        input = new Input { Path = "rootfile.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.DeleteFiles(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("UserName field must be of format domain\\username"));
    }

    [Test]
    public async Task DeleteFiles_FileNotFound_ReturnsSuccessFalse()
    {
        input = new Input
        {
            Path = "missing.txt",
        };

        options.ThrowErrorOnFailure = false;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(0));
        Assert.That(result.Error.Message, Does.Contain("STATUS_OBJECT_NAME_NOT_FOUND").IgnoreCase);
    }

    [Test]
    public async Task DeleteFiles_NoPermission_ReturnsFailure()
    {
        string restrictedPath = Path.Combine(testFilesPath, "restricted.txt");
        File.WriteAllText(restrictedPath, "cannot delete");
        File.SetAttributes(restrictedPath, FileAttributes.ReadOnly);

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input { Path = "restricted.txt" };
        options.ThrowErrorOnFailure = false;
        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("STATUS_ACCESS_DENIED").IgnoreCase);
    }

    [Test]
    public async Task DeleteFiles_EmptyPath_DeletesFilesInRoot()
    {
        string fileName = "root-test.txt";
        string filePath = Path.Combine(testFilesPath, fileName);
        await File.WriteAllTextAsync(filePath, "test");

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input
        {
            Path = string.Empty,
            Pattern = "root-test.txt",
        };
        options.ThrowErrorOnFailure = true;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(filePath), Is.False);
    }

    [Test]
    public async Task DeleteFiles_DirectoryPath_IgnoresDeepMatches()
    {
        string shallowFile = Path.Combine(testFilesPath, "deep", "match.txt");
        string deepFile = Path.Combine(testFilesPath, "deep", "inner", "match.txt");

        await File.WriteAllTextAsync(shallowFile, "delete me");
        await File.WriteAllTextAsync(deepFile, "keep me");

        await Task.Delay(TimeSpan.FromSeconds(1));

        input = new Input
        {
            Path = "deep",
            Pattern = "*.txt",
        };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(shallowFile), Is.False, "Shallow file should be deleted");
        Assert.That(File.Exists(deepFile), Is.True, "Deep file should not be deleted");
    }

    [Test]
    public async Task DeleteFiles_RegexPattern_DeletesMatchingFiles()
    {
        string dir = Path.Combine(testFilesPath, "regex-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "foo_123.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "foo_abc.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "bar_456.txt"), "x");

        input = new Input { Path = "regex-test", Pattern = "<regex>foo_\\d+\\.txt" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(result.FilesDeleted.Single().Name, Is.EqualTo("foo_123.txt"));
    }
}