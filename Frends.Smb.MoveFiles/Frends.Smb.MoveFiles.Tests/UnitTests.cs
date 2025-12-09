using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.MoveFiles.Definitions;
using NUnit.Framework;

namespace Frends.Smb.MoveFiles.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g., WSL2).
// They will not run on Windows natively because the OS reserves SMB port 445.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
[TestFixture]
public class MoveFilesTests
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
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "test-files-move");
        Directory.CreateDirectory(testFilesPath);

        Directory.CreateDirectory(Path.Combine(testFilesPath, "source"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "target"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source", "subdir1"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source", "subdir2"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source", "nested", "deep"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "preserve-test"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "preserve-test", "sub1"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "preserve-test", "sub2"));

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-move-{Guid.NewGuid()}")
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
        connection = new Connection { Server = serverName, Share = shareName, Username = user, Password = password };
        options = new Options
        {
            ThrowErrorOnFailure = true,
            ErrorMessageOnFailure = string.Empty,
            CreateTargetDirectories = true,
            IfTargetFileExists = FileExistsAction.Throw,
            PreserveDirectoryStructure = false,
        };
    }

    [TearDown]
    public async Task Cleanup()
    {
        if (sambaContainer is not null)
            await sambaContainer.ExecAsync(["sh", "-c", "chmod -R 0777 /share"]);

        foreach (var file in Directory.EnumerateFiles(testFilesPath, "*", SearchOption.AllDirectories))
            File.Delete(file);
    }

    [Test]
    public async Task MoveFiles_SingleFile_Success()
    {
        await CreateTestFileAsync("source/single.txt", "Move me!");

        input = new Input
        {
            SourcePath = "source/single.txt",
            TargetPath = "target",
        };

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(result.Files[0].SourcePath, Is.EqualTo("source\\single.txt"));
        Assert.That(result.Files[0].TargetPath, Is.EqualTo("target\\single.txt"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "single.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "single.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_PatternMatch_MovesMatchingFiles()
    {
        await CreateTestFileAsync("source/match1.txt", "file 1");
        await CreateTestFileAsync("source/match2.txt", "file 2");
        await CreateTestFileAsync("source/ignore.log", "keep");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "match1.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "match2.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "ignore.log")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "match1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "match2.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_RegexPattern_MovesMatchingFiles()
    {
        await CreateTestFileAsync("source/report_2024.txt", "x");
        await CreateTestFileAsync("source/report_old.txt", "x");
        await CreateTestFileAsync("source/data_2024.txt", "x");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Regex;
        options.Pattern = "report_\\d+\\.txt";

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(result.Files[0].SourcePath, Does.Contain("report_2024.txt"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "report_2024.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_PreserveDirectoryStructureFalse_FlattensFiles()
    {
        await CreateTestFileAsync("preserve-test/sub1/file1.txt", "content1");
        await CreateTestFileAsync("preserve-test/sub2/file2.txt", "content2");

        input = new Input
        {
            SourcePath = "preserve-test",
            TargetPath = "target",
        };

        options.PreserveDirectoryStructure = false;
        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "file1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "file2.txt")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "sub1")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "sub2")), Is.False);
    }

    [Test]
    public async Task MoveFiles_PreserveDirectoryStructureTrue_PreservesStructure()
    {
        await CreateTestFileAsync("preserve-test/sub1/file1.txt", "content1");
        await CreateTestFileAsync("preserve-test/sub2/file2.txt", "content2");

        input = new Input
        {
            SourcePath = "preserve-test",
            TargetPath = "target",
        };
        options.PreserveDirectoryStructure = true;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "sub1", "file1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "sub2", "file2.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_IfTargetFileExistsThrow_ThrowsException()
    {
        await CreateTestFileAsync("source/duplicate.txt", "source content");
        await CreateTestFileAsync("target/duplicate.txt", "target content");

        input = new Input
        {
            SourcePath = "source/duplicate.txt",
            TargetPath = "target",
        };
        options.IfTargetFileExists = FileExistsAction.Throw;

        var ex = Assert.Throws<Exception>(() =>
                Smb.MoveFiles(input, connection, options, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain("already exists"));
        Assert.That(ex.Message, Does.Contain("No files moved"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "duplicate.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_IfTargetFileExistsOverwrite_OverwritesFile()
    {
        await CreateTestFileAsync("source/overwrite.txt", "new content");
        await CreateTestFileAsync("target/overwrite.txt", "old content");

        input = new Input
        {
            SourcePath = "source/overwrite.txt",
            TargetPath = "target",
        };
        options.IfTargetFileExists = FileExistsAction.Overwrite;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "overwrite.txt")), Is.False);
        var content = await File.ReadAllTextAsync(Path.Combine(testFilesPath, "target", "overwrite.txt"));
        Assert.That(content, Is.EqualTo("new content"));
    }

    [Test]
    public async Task MoveFiles_IfTargetFileExistsRename_RenamesFile()
    {
        await CreateTestFileAsync("source/rename.txt", "source content");
        await CreateTestFileAsync("target/rename.txt", "existing content");

        input = new Input
        {
            SourcePath = "source/rename.txt",
            TargetPath = "target",
        };
        options.IfTargetFileExists = FileExistsAction.Rename;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(result.Files[0].TargetPath, Does.Match(@"target\\rename\(\d+\)\.txt"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "rename.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "rename(1).txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_CreateTargetDirectoriesTrue_CreatesDirectories()
    {
        await CreateTestFileAsync("source/test.txt", "content");

        input = new Input
        {
            SourcePath = "source/test.txt",
            TargetPath = "newdir/subdir",
        };

        options.CreateTargetDirectories = true;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "newdir", "subdir")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "newdir", "subdir", "test.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_CreateTargetDirectoriesFalse_FailsIfDirectoryMissing()
    {
        await CreateTestFileAsync("source/test.txt", "content");

        input = new Input
        {
            SourcePath = "source/test.txt",
            TargetPath = "nonexistent/path",
        };
        options.CreateTargetDirectories = false;
        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "test.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_DuplicateTargetPaths_ThrowsException()
    {
        await CreateTestFileAsync("source/subdir1/file.txt", "content1");
        await CreateTestFileAsync("source/subdir2/file.txt", "content2");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";
        options.PreserveDirectoryStructure = false;
        options.IfTargetFileExists = FileExistsAction.Throw;

        var ex = Assert.Throws<Exception>(() =>
                 Smb.MoveFiles(input, connection, options, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain("No files moved."));
    }

    [Test]
    public async Task MoveFiles_RollbackOnFailure_DeletesCopiedFiles()
    {
        await CreateTestFileAsync("source/file1.txt", "content1");
        await CreateTestFileAsync("source/file2.txt", "content2");
        await CreateTestFileAsync("target/file2.txt", "existing");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";
        options.IfTargetFileExists = FileExistsAction.Throw;
        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);

        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "file1.txt")), Is.False);

        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "file1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "file2.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_NoMatchingFiles_ReturnsFailure()
    {
        await CreateTestFileAsync("source/file.log", "content");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";
        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("No files found"));
    }

    [Test]
    public void MoveFiles_InvalidSourcePath_ReturnsFailure()
    {
        input = new Input
        {
            SourcePath = "nonexistent",
            TargetPath = "target",
        };
        options.ThrowErrorOnFailure = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public async Task MoveFiles_EmptySourcePath_MovesFromRoot()
    {
        await CreateTestFileAsync("root-file.txt", "root content");

        input = new Input
        {
            SourcePath = string.Empty,
            TargetPath = "target",
        };

        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "root-file.txt";

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "root-file.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "root-file.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_OnlyShallowFiles_IgnoresNestedFiles()
    {
        await CreateTestFileAsync("source/shallow.txt", "shallow");
        await CreateTestFileAsync("source/nested/deep/deep.txt", "deep");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };

        options.Recursive = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "shallow.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "nested", "deep", "deep.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_RecursiveWithPreserve_PreservesNestedStructure()
    {
        await CreateTestFileAsync("source/level1/level2/deep.txt", "deep content");
        await CreateTestFileAsync("source/level1/shallow.txt", "shallow content");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };
        options.Recursive = true;
        options.PreserveDirectoryStructure = true;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "level1", "level2", "deep.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "level1", "shallow.txt")), Is.True);
    }

    [Test]
    public async Task MoveFiles_NonRecursive_OnlyMovesRootLevelFiles()
    {
        await CreateTestFileAsync("source/root.txt", "root");
        await CreateTestFileAsync("source/subdir/nested.txt", "nested");

        input = new Input
        {
            SourcePath = "source",
            TargetPath = "target",
        };
        options.Recursive = false;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(1));
        Assert.That(result.Files[0].SourcePath, Does.Contain("root.txt"));
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "root.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "subdir", "nested.txt")), Is.True, "Nested file should remain");
    }

    [Test]
    public async Task MoveFiles_EmptySourcePathWithPreserveStructure_PreservesDirectoryTree()
    {
        await CreateTestFileAsync("level1/level2/file1.txt", "content1");
        await CreateTestFileAsync("level1/file2.txt", "content2");
        await CreateTestFileAsync("another/file3.txt", "content3");

        input = new Input
        {
            SourcePath = string.Empty,
            TargetPath = "backup",
        };
        options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        options.Pattern = "*.txt";
        options.PreserveDirectoryStructure = true;
        options.Recursive = true;

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(3));

        Assert.That(File.Exists(Path.Combine(testFilesPath, "backup", "level1", "level2", "file1.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "backup", "level1", "file2.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "backup", "another", "file3.txt")), Is.True);

        Assert.That(File.Exists(Path.Combine(testFilesPath, "level1", "level2", "file1.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "level1", "file2.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "another", "file3.txt")), Is.False);
    }

    private async Task CreateTestFileAsync(string relativePath, string content)
    {
        string fullPath = Path.Combine(testFilesPath, relativePath);

        string dirPath = Path.GetDirectoryName(fullPath);
        if (dirPath != null)
            Directory.CreateDirectory(dirPath);

        await File.WriteAllTextAsync(fullPath, content);

        await sambaContainer.ExecAsync(["sh", "-c", "chmod -R 0777 /share"]);
    }
}
