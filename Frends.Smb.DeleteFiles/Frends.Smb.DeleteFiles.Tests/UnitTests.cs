using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.DeleteFiles.Definitions;
using NUnit.Framework;

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
        Directory.CreateDirectory(Path.Combine(testFilesPath, "regex-test"));
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "rootfile.txt"), "root");

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-delete-{Guid.NewGuid()}")
            .WithBindMount(testFilesPath, "/share")
            .WithPortBinding(445, 445)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(445))
            .WithCommand(
                "-n",
                "-u",
                "testuser;testpass",
                "-s",
                "testshare;/share;no;no;no;testuser;root",
                "-w",
                "WORKGROUP")
            .Build();

        await sambaContainer.StartAsync();
        await sambaContainer.ExecAsync(["sh", "-c", "chmod -R 0777 /share"]);
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (sambaContainer != null)
        {
            await sambaContainer.DisposeAsync();
        }

        Directory.Delete(testFilesPath, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            Server = serverName, Share = shareName, Username = userName, Password = password,
        };

        options = new Options { ThrowErrorOnFailure = true, ErrorMessageOnFailure = string.Empty, };
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(testFilesPath, "*", SearchOption.AllDirectories))
            File.Delete(file);
    }

    [Test]
    public async Task DeleteFiles_DirectFilePath_Success()
    {
        await CreateTestFileAsync("single-target.txt", "Delete me!");

        input = new Input { Path = "single-target.txt" };
        options.ThrowErrorOnFailure = true;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(result.FilesDeleted.Single().Name, Is.EqualTo("single-target.txt"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "single-target.txt")), Is.False);
    }

    [Test]
    public async Task DeleteFiles_PatternMatch_SuccessfullyDeletesMatchingFiles()
    {
        await CreateTestFileAsync("pattern-test/match1.txt", "to delete");
        await CreateTestFileAsync("pattern-test/match2.txt", "to delete");
        await CreateTestFileAsync("pattern-test/ignore.log", "keep");

        input = new Input { Path = "pattern-test", Pattern = "*.txt" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(2));
        Assert.That(result.FilesDeleted.Any(f => f.Name == "match1.txt"), Is.True);
        Assert.That(result.FilesDeleted.Any(f => f.Name == "match2.txt"), Is.True);
    }

    [Test]
    public async Task DeleteFiles_DoesNotTouchSubdirectories()
    {
        await CreateTestFileAsync("subdir-test/root.txt", "delete me");
        await CreateTestFileAsync("subdir-test/nested/inner.txt", "keep me");

        input = new Input { Path = "subdir-test", Pattern = "*.txt" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "subdir-test", "root.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "subdir-test", "nested", "inner.txt")), Is.True);
    }

    [Test]
    public async Task DeleteFiles_DoesNotDeleteDirectories()
    {
        await CreateTestFileAsync("dirtest/file1.txt", "delete me");

        input = new Input { Path = "dirtest", Pattern = "*" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "dirtest", "nested")), Is.True);
    }

    [Test]
    public void DeleteFiles_InvalidUsernameFormat_ThrowsArgumentException()
    {
        connection.Username = "invalidUser";
        input = new Input { Path = "rootfile.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.DeleteFiles(input, connection, options, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain("UserName field must be of format domain\\username"));
    }

    [Test]
    public async Task DeleteFiles_FileNotFound_ReturnsSuccessFalse()
    {
        input = new Input { Path = "missing.txt" };
        options.ThrowErrorOnFailure = false;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(0));
        Assert.That(result.Error.Message, Does.Contain("STATUS_OBJECT_NAME_NOT_FOUND").IgnoreCase);
    }

    [Test]
    public async Task DeleteFiles_NoPermission_ReturnsFailure()
    {
        await CreateTestFileAsync("restricted.txt", "cannot delete");
        File.SetAttributes(Path.Combine(testFilesPath, "restricted.txt"), FileAttributes.ReadOnly);

        input = new Input { Path = "restricted.txt" };
        options.ThrowErrorOnFailure = false;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("STATUS_ACCESS_DENIED").IgnoreCase);
    }

    [Test]
    public async Task DeleteFiles_EmptyPath_DeletesFilesInRoot()
    {
        await CreateTestFileAsync("root-test.txt", "test");

        input = new Input { Path = string.Empty, Pattern = "root-test.txt" };
        options.ThrowErrorOnFailure = true;

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "root-test.txt")), Is.False);
    }

    [Test]
    public async Task DeleteFiles_DirectoryPath_IgnoresDeepMatches()
    {
        await CreateTestFileAsync("deep/match.txt", "delete me");
        await CreateTestFileAsync("deep/inner/match.txt", "keep me");

        input = new Input { Path = "deep", Pattern = "*.txt" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "deep", "match.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "deep", "inner", "match.txt")), Is.True);
    }

    [Test]
    public async Task DeleteFiles_RegexPattern_DeletesMatchingFiles()
    {
        await CreateTestFileAsync("regex-test/foo_123.txt", "x");
        await CreateTestFileAsync("regex-test/foo_abc.txt", "x");
        await CreateTestFileAsync("regex-test/bar_456.txt", "x");

        input = new Input { Path = "regex-test", Pattern = "<regex>foo_\\d+\\.txt" };

        var result = await Smb.DeleteFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalFilesDeleted, Is.EqualTo(1));
        Assert.That(result.FilesDeleted.Single().Name, Is.EqualTo("foo_123.txt"));
    }

    private async Task CreateTestFileAsync(string relativePath, string content)
    {
        string fullPath = Path.Combine(testFilesPath, relativePath);

        string dirPath = Path.GetDirectoryName(fullPath);
        if (dirPath != null)
            Directory.CreateDirectory(dirPath);

        await File.WriteAllTextAsync(fullPath, content);
        await sambaContainer.ExecAsync(["sh", "-c", $"chmod 0777 '/share/{relativePath}'"]);
    }
}
