using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.RenameFile.Definitions;
using NUnit.Framework;

namespace Frends.Smb.RenameFile.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g. WSL2).
// They will not run on Windows natively because SMB port 445 is reserved by the OS.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
[TestFixture]
public class RenameFileTests
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
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "rename-tests");
        Directory.CreateDirectory(testFilesPath);

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-rename-server-{Guid.NewGuid()}")
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
    }

    [SetUp]
    public async Task Setup()
    {
        // Clean up any files from previous tests first
        if (File.Exists(LocalPath("renamed.txt"))) File.Delete(LocalPath("renamed.txt"));
        if (File.Exists(LocalPath("duplicate(1).txt"))) File.Delete(LocalPath("duplicate(1).txt"));
        if (File.Exists(LocalPath("duplicate(2).txt"))) File.Delete(LocalPath("duplicate(2).txt"));
        if (File.Exists(LocalPath("duplicate(3).txt"))) File.Delete(LocalPath("duplicate(3).txt"));
        if (File.Exists(LocalPath("file1-renamed.txt"))) File.Delete(LocalPath("file1-renamed.txt"));
        if (File.Exists(LocalPath("file1-final.txt"))) File.Delete(LocalPath("file1-final.txt"));

        File.WriteAllText(LocalPath("file1.txt"), "File One");
        File.WriteAllText(LocalPath("file2.txt"), "File Two");
        File.WriteAllText(LocalPath("duplicate.txt"), "Duplicate file");

        await sambaContainer.ExecAsync(new[] { "chmod", "777", "/share/file1.txt" });
        await sambaContainer.ExecAsync(new[] { "chmod", "777", "/share/file2.txt" });
        await sambaContainer.ExecAsync(new[] { "chmod", "777", "/share/duplicate.txt" });

        await Task.Delay(100);

        connection = new Connection
        {
            Server = serverName,
            Share = shareName,
            Username = userName,
            Password = password,
        };

        options = new Options
        {
            ThrowErrorOnFailure = false,
            ErrorMessageOnFailure = string.Empty,
            RenameBehaviour = RenameBehaviour.Throw,
        };
    }

    [Test]
    public void RenameFile_ThrowIfExists_FileAlreadyExists_ShouldFail()
    {
        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;

        var result = Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("File already exists"));
        Assert.That(File.Exists(LocalPath("file1.txt")), Is.True);
        Assert.That(File.Exists(LocalPath("duplicate.txt")), Is.True);
    }

    [Test]
    public async Task RenameFile_OverwriteIfExists_ShouldReplaceFile()
    {
        await sambaContainer.ExecAsync(new[] { "chmod", "777", $"/share/file1.txt" });
        await sambaContainer.ExecAsync(new[] { "chmod", "777", $"/share/duplicate.txt" });

        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Overwrite;
        var result = Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(LocalPath("duplicate.txt")), Is.True);
        Assert.That(File.ReadAllText(LocalPath("duplicate.txt")), Does.Contain("File One"));
    }

    [Test]
    public void RenameFile_RenameIfExists_ShouldGenerateNewName()
    {
        input = new Input
        {
            Path = @"duplicate.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Rename;

        var result1 = Smb.RenameFile(input, connection, options, CancellationToken.None);
        Assert.That(result1.Success, Is.True);
        Assert.That(result1.NewFilePath, Does.Contain("duplicate(1).txt"));
        Assert.That(File.Exists(LocalPath("duplicate(1).txt")), Is.True);

        File.WriteAllText(LocalPath("duplicate.txt"), "Duplicate file");
        await sambaContainer.ExecAsync(new[] { "chmod", "777", "/share/duplicate.txt" });

        var result2 = Smb.RenameFile(input, connection, options, CancellationToken.None);
        Console.WriteLine(result2.Error.Message);
        Assert.That(result2.Success, Is.True);
        Assert.That(result2.NewFilePath, Does.Contain("duplicate(2).txt"));
        Assert.That(File.Exists(LocalPath("duplicate(2).txt")), Is.True);

        File.WriteAllText(LocalPath("duplicate.txt"), "Duplicate file");
        await sambaContainer.ExecAsync(new[] { "chmod", "777", "/share/duplicate.txt" });

        var result3 = Smb.RenameFile(input, connection, options, CancellationToken.None);
        Assert.That(result3.Success, Is.True);
        Assert.That(result3.NewFilePath, Does.Contain("duplicate(3).txt"));
        Assert.That(File.Exists(LocalPath("duplicate(3).txt")), Is.True);

        Assert.That(File.Exists(LocalPath("duplicate(1).txt")), Is.True);
        Assert.That(File.Exists(LocalPath("duplicate(2).txt")), Is.True);
        Assert.That(File.Exists(LocalPath("duplicate(3).txt")), Is.True);
    }

    [Test]
    public void RenameFile_SimpleRename_ShouldSucceed()
    {
        input = new Input
        {
            Path = @"file2.txt",
            NewFileName = "renamed.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;

        var result = Smb.RenameFile(input, connection, options, CancellationToken.None);
        Console.WriteLine(result.Error.Message);
        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(LocalPath("renamed.txt")), Is.True);
        Assert.That(File.Exists(LocalPath("file2.txt")), Is.False);
    }

    [Test]
    public void RenameFile_InvalidPath_ShouldFail()
    {
        input = new Input
        {
            Path = @"nonexistent.txt",
            NewFileName = "newfile.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;
        var result = Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("Failed to open file"));
    }

    [Test]
    public void RenameFile_MultipleSequentialRenames_ShouldWork()
    {
        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "file1-renamed.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Overwrite;

        var result1 = Smb.RenameFile(input, connection, options, CancellationToken.None);
        Console.WriteLine(result1.Error.Message);
        Assert.That(result1.Success, Is.True);
        Assert.That(File.Exists(LocalPath("file1-renamed.txt")), Is.True);

        input.Path = "file1-renamed.txt";
        input.NewFileName = "file1-final.txt";

        var result2 = Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result2.Success, Is.True);
        Assert.That(File.Exists(LocalPath("file1-final.txt")), Is.True);
    }

    private string LocalPath(string relative) => Path.Combine(testFilesPath, relative);
}