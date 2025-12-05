using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.MoveDirectory.Definitions;
using NUnit.Framework;

namespace Frends.Smb.MoveDirectory.Tests;

[TestFixture]
public class MoveDirectoryTests
{
    private readonly string serverName = "127.0.0.1";
    private readonly string shareName = "testshare";
    private readonly string user = "WORKGROUP\\testuser";
    private readonly string password = "testpass";

    private Input input;
    private Connection connection;
    private Options options;
    private IContainer sambaContainer;
    private string testFilesPath;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "test-files-move-directory");
        Directory.CreateDirectory(testFilesPath);

        Directory.CreateDirectory(Path.Combine(testFilesPath, "source"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "target"));

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-move-dir-{Guid.NewGuid()}")
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
            await sambaContainer.ExecAsync(["sh", "-c", "chmod -R 0777 /share"]);
            await sambaContainer.DisposeAsync();
        }

        Directory.Delete(testFilesPath, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            Server = serverName,
            Share = shareName,
            Username = user,
            Password = password,
        };

        options = new Options
        {
            ThrowErrorOnFailure = true,
            ErrorMessageOnFailure = string.Empty,
            IfTargetDirectoryExists = DirectoryExistsAction.Throw,
        };
    }

    [TearDown]
    public async Task Cleanup()
    {
        if (sambaContainer is not null)
            await sambaContainer.ExecAsync(["sh", "-c", "chmod -R 0777 /share"]);

        foreach (var dir in Directory.EnumerateDirectories(testFilesPath))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName != "source" && dirName != "target")
            {
                Directory.Delete(dir, true);
            }
        }

        CleanDirectory(Path.Combine(testFilesPath, "source"));
        CleanDirectory(Path.Combine(testFilesPath, "target"));
    }

    [Test]
    public async Task MoveDirectory_EmptyDirectory_Success()
    {
        await CreateDirectoryAsync("source/empty-dir");

        input = new Input
        {
            SourcePath = "source/empty-dir",
            TargetPath = "target/moved-empty",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.SourcePath, Is.EqualTo("source\\empty-dir"));
        Assert.That(result.TargetPath, Is.EqualTo("target\\moved-empty"));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "empty-dir")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "moved-empty")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_WithFiles_MovesEverything()
    {
        await CreateDirectoryAsync("source/with-files");
        await CreateTestFileAsync("source/with-files/file1.txt", "content1");
        await CreateTestFileAsync("source/with-files/file2.txt", "content2");

        input = new Input
        {
            SourcePath = "source/with-files",
            TargetPath = "target/moved-files",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "with-files")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "moved-files")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-files", "file1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-files", "file2.txt")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_WithNestedStructure_PreservesStructure()
    {
        await CreateDirectoryAsync("source/nested");
        await CreateDirectoryAsync("source/nested/level1");
        await CreateDirectoryAsync("source/nested/level1/level2");
        await CreateTestFileAsync("source/nested/root.txt", "root");
        await CreateTestFileAsync("source/nested/level1/mid.txt", "mid");
        await CreateTestFileAsync("source/nested/level1/level2/deep.txt", "deep");

        input = new Input
        {
            SourcePath = "source/nested",
            TargetPath = "target/moved-nested",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "nested")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-nested", "root.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-nested", "level1", "mid.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-nested", "level1", "level2", "deep.txt")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_IfTargetExistsThrow_ThrowsException()
    {
        await CreateDirectoryAsync("source/dir-to-move");
        await CreateTestFileAsync("source/dir-to-move/file.txt", "content");
        await CreateDirectoryAsync("target/dir-to-move");

        input = new Input
        {
            SourcePath = "source/dir-to-move",
            TargetPath = "target/dir-to-move",
        };

        options.IfTargetDirectoryExists = DirectoryExistsAction.Throw;

        var ex = Assert.Throws<IOException>(() =>
            Smb.MoveDirectory(input, connection, options, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain("already exists"));
        Assert.That(ex.Message, Does.Contain("No directory moved"));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "dir-to-move")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "dir-to-move", "file.txt")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_IfTargetExistsOverwrite_DeletesTargetAndMoves()
    {
        await CreateDirectoryAsync("source/overwrite-source");
        await CreateTestFileAsync("source/overwrite-source/new.txt", "new content");

        await CreateDirectoryAsync("target/overwrite-target");
        await CreateTestFileAsync("target/overwrite-target/old.txt", "old content");

        input = new Input
        {
            SourcePath = "source/overwrite-source",
            TargetPath = "target/overwrite-target",
        };

        options.IfTargetDirectoryExists = DirectoryExistsAction.Overwrite;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "overwrite-source")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "overwrite-target")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "overwrite-target", "new.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "overwrite-target", "old.txt")), Is.False);
    }

    [Test]
    public async Task MoveDirectory_IfTargetExistsRename_CreatesNewName()
    {
        await CreateDirectoryAsync("source/rename-source");
        await CreateTestFileAsync("source/rename-source/file.txt", "content");

        await CreateDirectoryAsync("target/rename-target");

        input = new Input
        {
            SourcePath = "source/rename-source",
            TargetPath = "target/rename-target",
        };

        options.IfTargetDirectoryExists = DirectoryExistsAction.Rename;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TargetPath, Does.Match(@"target\\rename-target\(\d+\)"));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "rename-source")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "rename-target")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "rename-target(1)")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "rename-target(1)", "file.txt")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_MultipleRenames_IncrementsCounter()
    {
        await CreateDirectoryAsync("source/multi-source");
        await CreateDirectoryAsync("target/multi-target");
        await CreateDirectoryAsync("target/multi-target(1)");
        await CreateDirectoryAsync("target/multi-target(2)");

        input = new Input
        {
            SourcePath = "source/multi-source",
            TargetPath = "target/multi-target",
        };

        options.IfTargetDirectoryExists = DirectoryExistsAction.Rename;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TargetPath, Is.EqualTo("target\\multi-target(3)"));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "multi-target(3)")), Is.True);
    }

    [Test]
    public void MoveDirectory_SourceDoesNotExist_ReturnsFailure()
    {
        input = new Input
        {
            SourcePath = "source/nonexistent",
            TargetPath = "target/destination",
        };

        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.Message, Does.Contain("does not exist"));
    }

    [Test]
    public void MoveDirectory_EmptySourcePath_ThrowsException()
    {
        input = new Input
        {
            SourcePath = string.Empty,
            TargetPath = "target/destination",
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Smb.MoveDirectory(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("SourcePath cannot be empty"));
    }

    [Test]
    public async Task MoveDirectory_EmptyTargetPath_ThrowsException()
    {
        await CreateDirectoryAsync("source/test-dir");

        input = new Input
        {
            SourcePath = "source/test-dir",
            TargetPath = string.Empty,
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Smb.MoveDirectory(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("TargetPath cannot be empty"));
    }

    [Test]
    public void MoveDirectory_UNCPathAsSource_ThrowsException()
    {
        input = new Input
        {
            SourcePath = @"\\server\share\path",
            TargetPath = "target/destination",
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Smb.MoveDirectory(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("should be relative to the share"));
    }

    [Test]
    public async Task MoveDirectory_UNCPathAsTarget_ThrowsException()
    {
        await CreateDirectoryAsync("source/test-dir");

        input = new Input
        {
            SourcePath = "source/test-dir",
            TargetPath = @"\\server\share\path",
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Smb.MoveDirectory(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("should be relative to the share"));
    }

    [Test]
    public async Task MoveDirectory_CreatesParentDirectories_Success()
    {
        await CreateDirectoryAsync("source/dir-to-move");
        await CreateTestFileAsync("source/dir-to-move/file.txt", "content");

        input = new Input
        {
            SourcePath = "source/dir-to-move",
            TargetPath = "target/level1/level2/moved-dir",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "level1", "level2", "moved-dir")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "level1", "level2", "moved-dir", "file.txt")), Is.True);
    }

    [Test]
    public async Task MoveDirectory_WithForwardSlashes_NormalizesPath()
    {
        await CreateDirectoryAsync("source/slash-test");
        await CreateTestFileAsync("source/slash-test/file.txt", "content");

        input = new Input
        {
            SourcePath = "source/slash-test",
            TargetPath = "target/moved/slash/test",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TargetPath, Does.Contain("\\"));
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "moved", "slash", "test")), Is.True);
    }

    [Test]
    public void MoveDirectory_ThrowErrorOnFailureFalse_ReturnsErrorInResult()
    {
        input = new Input
        {
            SourcePath = "source/nonexistent",
            TargetPath = "target/destination",
        };

        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public void MoveDirectory_CustomErrorMessage_UsesCustomMessage()
    {
        input = new Input
        {
            SourcePath = "source/nonexistent",
            TargetPath = "target/destination",
        };

        options.ThrowErrorOnFailure = false;
        options.ErrorMessageOnFailure = "Custom error occurred";

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("Custom error occurred"));
    }

    [Test]
    public async Task MoveDirectory_LargeDirectoryTree_MovesSuccessfully()
    {
        await CreateDirectoryAsync("source/large-tree");
        for (int i = 0; i < 5; i++)
        {
            await CreateDirectoryAsync($"source/large-tree/subdir{i}");
            for (int j = 0; j < 3; j++)
            {
                await CreateTestFileAsync($"source/large-tree/subdir{i}/file{j}.txt", $"content {i}-{j}");
            }
        }

        input = new Input
        {
            SourcePath = "source/large-tree",
            TargetPath = "target/moved-large",
        };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "large-tree")), Is.False);

        for (int i = 0; i < 5; i++)
        {
            Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "moved-large", $"subdir{i}")), Is.True);
            for (int j = 0; j < 3; j++)
            {
                Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "moved-large", $"subdir{i}", $"file{j}.txt")), Is.True);
            }
        }
    }

    [Test]
    public async Task MoveDirectory_OverwriteWithNestedStructure_DeletesAllOldContent()
    {
        await CreateDirectoryAsync("source/overwrite-nested-src");
        await CreateDirectoryAsync("source/overwrite-nested-src/new-subdir");
        await CreateTestFileAsync("source/overwrite-nested-src/new-file.txt", "new");
        await CreateTestFileAsync("source/overwrite-nested-src/new-subdir/nested-new.txt", "new nested");

        await CreateDirectoryAsync("target/overwrite-nested-tgt");
        await CreateDirectoryAsync("target/overwrite-nested-tgt/old-subdir");
        await CreateTestFileAsync("target/overwrite-nested-tgt/old-file.txt", "old");
        await CreateTestFileAsync("target/overwrite-nested-tgt/old-subdir/nested-old.txt", "old nested");

        input = new Input
        {
            SourcePath = "source/overwrite-nested-src",
            TargetPath = "target/overwrite-nested-tgt",
        };

        options.IfTargetDirectoryExists = DirectoryExistsAction.Overwrite;

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);

        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "overwrite-nested-tgt", "old-file.txt")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "overwrite-nested-tgt", "old-subdir")), Is.False);

        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "overwrite-nested-tgt", "new-file.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "overwrite-nested-tgt", "new-subdir", "nested-new.txt")), Is.True);
    }

    private void CleanDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.Delete(file);

        foreach (var dir in Directory.EnumerateDirectories(path))
            Directory.Delete(dir, true);
    }

    private async Task CreateDirectoryAsync(string relativePath)
    {
        string fullPath = Path.Combine(testFilesPath, relativePath);
        Directory.CreateDirectory(fullPath);
        await sambaContainer.ExecAsync(["sh", "-c", $"chmod 0777 '/share/{relativePath.Replace('\\', '/')}'"]);
    }

    private async Task CreateTestFileAsync(string relativePath, string content)
    {
        string fullPath = Path.Combine(testFilesPath, relativePath);

        string dirPath = Path.GetDirectoryName(fullPath);
        if (dirPath != null)
            Directory.CreateDirectory(dirPath);

        await File.WriteAllTextAsync(fullPath, content);
        await sambaContainer.ExecAsync(["sh", "-c", $"chmod 0777 '/share/{relativePath.Replace('\\', '/')}'"]);
    }
}
