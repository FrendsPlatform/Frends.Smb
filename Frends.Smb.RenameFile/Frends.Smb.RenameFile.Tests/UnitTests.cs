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
        File.WriteAllText(Path.Combine(testFilesPath, "file1.txt"), "File One");
        File.WriteAllText(Path.Combine(testFilesPath, "file2.txt"), "File Two");
        File.WriteAllText(Path.Combine(testFilesPath, "duplicate.txt"), "Duplicate file");

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
    public void Setup()
    {
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
    public async Task RenameFile_ThrowIfExists_FileAlreadyExists_ShouldFail()
    {
        ResetTestFiles();

        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;

        var result = await Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("File already exists"));
        Assert.That(File.Exists(LocalPath("file1.txt")), Is.True);
        Assert.That(File.Exists(LocalPath("duplicate.txt")), Is.True);
    }

    [Test]
    public async Task RenameFile_OverwriteIfExists_ShouldReplaceFile()
    {
        ResetTestFiles();

        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Overwrite;

        var result = await Smb.RenameFile(input, connection, options, CancellationToken.None);
        Console.WriteLine($"Error.Message: {result.Error.Message}, AddInfo: {result.Error.AdditionalInfo}");
        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(LocalPath("duplicate.txt")), Is.True);
        Assert.That(File.ReadAllText(LocalPath("duplicate.txt")), Does.Contain("File One"));
    }

    [Test]
    public async Task RenameFile_RenameIfExists_ShouldGenerateNewName()
    {
        ResetTestFiles();

        input = new Input
        {
            Path = @"duplicate.txt",
            NewFileName = "duplicate.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Rename;

        var result = await Smb.RenameFile(input, connection, options, CancellationToken.None);
        Console.WriteLine($"Error.Message: {result.Error.Message}, AddInfo: {result.Error.AdditionalInfo}");
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewFilePath, Does.Contain("(1)"));
        Assert.That(File.Exists(LocalPath("duplicate(1).txt")), Is.True);
    }

    [Test]
    public async Task RenameFile_SimpleRename_ShouldSucceed()
    {
        ResetTestFiles();

        input = new Input
        {
            Path = @"file2.txt",
            NewFileName = "renamed.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;

        var result = await Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(LocalPath("renamed.txt")), Is.True);
        Assert.That(File.Exists(LocalPath("file2.txt")), Is.False);
    }

    [Test]
    public async Task RenameFile_InvalidPath_ShouldFail()
    {
        input = new Input
        {
            Path = @"nonexistent.txt",
            NewFileName = "newfile.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Throw;

        var result = await Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("Failed to open file"));
    }

    [Test]
    public async Task RenameFile_MultipleSequentialRenames_ShouldWork()
    {
        ResetTestFiles();

        input = new Input
        {
            Path = @"file1.txt",
            NewFileName = "file1-renamed.txt",
        };

        options.RenameBehaviour = RenameBehaviour.Overwrite;

        var result1 = await Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result1.Success, Is.True);

        input.Path = "file1-renamed.txt";
        input.NewFileName = "file1-final.txt";

        var result2 = await Smb.RenameFile(input, connection, options, CancellationToken.None);

        Assert.That(result2.Success, Is.True);
        Assert.That(File.Exists(LocalPath("file1-final.txt")), Is.True);
    }

    private string LocalPath(string relative) => Path.Combine(testFilesPath, relative);

    private void ResetTestFiles()
    {
        File.WriteAllText(LocalPath("file1.txt"), "File One");
        File.WriteAllText(LocalPath("file2.txt"), "File Two");
        File.WriteAllText(LocalPath("duplicate.txt"), "Duplicate file");
        if (File.Exists(LocalPath("renamed.txt"))) File.Delete(LocalPath("renamed.txt"));
        if (File.Exists(LocalPath("duplicate(1).txt"))) File.Delete(LocalPath("duplicate(1).txt"));
    }
}