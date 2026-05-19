using System;
using System.IO;
using System.Linq;
using System.Threading;
using Frends.Smb.CopyFiles.Definitions;
using NUnit.Framework;

namespace Frends.Smb.CopyFiles.Tests;

[TestFixture]
public class CopyFilesTests : SmbTestBase
{
    [TestCase("src/test1.txt", PatternMatchingMode.Regex, "", 1)]
    [TestCase("src", PatternMatchingMode.Regex, "^test.*\\.txt", 4)]
    [TestCase("src", PatternMatchingMode.Wildcards, "*.txt", 5)]
    [TestCase("src", PatternMatchingMode.Wildcards, "test?.*", 6)]
    public void CopySpecificFileUsingPattern(
        string sourcePath,
        PatternMatchingMode patternMatchingMode,
        string pattern,
        int expectedCopiesCount)
    {
        Input.SourcePath = sourcePath;
        Input.TargetPath = "dst/src";
        Options.PatternMatchingMode = patternMatchingMode;
        Options.Pattern = pattern;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Files.Count, Is.EqualTo(expectedCopiesCount));
        var resultDir = File.Exists(Path.Combine(TestDirPath, sourcePath))
            ? Path.Combine(TestDirPath, "dst", "src")
            : Path.Combine(TestDirPath, "dst", "src", "src");
        var filesCount = Directory.GetFiles(resultDir).Length;
        Assert.That(filesCount, Is.EqualTo(expectedCopiesCount));
    }

    [Test]
    public void CreateTargetDirectoryIsTrue()
    {
        Options.CreateTargetDirectories = true;
        Input.SourcePath = "src/subDir";
        Options.Recursive = false;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
    }

    [Test]
    public void CreateTargetDirectoryIsFalse()
    {
        Options.CreateTargetDirectories = false;
        Input.SourcePath = "src/subDir";
        Options.Recursive = false;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(
            result.Error.Message,
            Contains.Substring("Target directory does not exist and Options.CreateTargetDirectories is disabled."));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.False);
    }

    [Test]
    public void FileExistsActionIsThrow()
    {
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        PathString expectedMessage = @"File dst\old.foo already exists.";
        PathString actualMessage = result.Error.Message;
        Assert.That(actualMessage.Value, Contains.Substring(expectedMessage));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
    }

    [Test]
    public void RollbackTmpFilesWhenErrorOccurred()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        PathString path = "src/error";
        Input.SourcePath = path.Value;
        Options.Recursive = true;
        Options.CreateTargetDirectories = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "error", "old.foo")), Is.True);
        Assert.That(
            File.ReadAllText(Path.Combine(TestDirPath, "dst", "error", "old.foo")),
            Is.EqualTo("old test content"));
    }

    [Test]
    public void RemoveTmpFilesWhenFinishOverwriting()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        PathString path = "src/error";
        Input.SourcePath = path.Value;
        Options.CreateTargetDirectories = false;
        Options.Recursive = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "error", "old.foo")), Is.True);
        Assert.That(Directory.GetFiles(Path.Combine(TestDirPath, "dst", "error")).Length, Is.EqualTo(1));
    }

    [Test]
    public void RemoveNewFilesWhenErrorOccured()
    {
        Options.IfTargetFileExists = FileExistsAction.Rename;
        Input.SourcePath = "src/error";
        Options.Recursive = true;
        Options.CreateTargetDirectories = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "error", "old.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "error", "old(1).foo")), Is.False);
    }

    [Test]
    public void FileExistsActionIsOverwrite()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old.foo")), Is.EqualTo("new test content"));
    }

    [Test]
    public void FileExistsActionIsRename()
    {
        Options.IfTargetFileExists = FileExistsAction.Rename;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old.foo")), Is.EqualTo("old test content"));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old(1).foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old(1).foo")), Is.EqualTo("new test content"));
    }

    [Test]
    public void PreserveDirectoryStructureIsTrue()
    {
        Options.PreserveDirectoryStructure = true;
        Options.Recursive = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.True);
    }

    [Test]
    public void PreserveDirectoryStructureIsFalse()
    {
        Options.PreserveDirectoryStructure = false;
        Options.Recursive = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        PathString expectedMessage = @"File dst\sub.foo already exists.";
        Assert.That(result.Error.Message, Contains.Substring(expectedMessage));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.False);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }

    [Test]
    public void CopyRecursiveIsTrue()
    {
        Options.Recursive = true;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.True);
    }

    [Test]
    public void CopyRecursiveIsFalse()
    {
        Options.Recursive = false;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_PartialSuccess_ReturnsSuccessWithFailures()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = true;
        Options.ThrowErrorOnFailure = false;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PreserveDirectoryStructure = false;
        Options.Recursive = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.FileFailures, Is.Not.Empty);
        Assert.That(result.Error.FileFailures.Any(f => f.SourcePath.Contains("old.foo")), Is.True);
        Assert.That(result.Error.AdditionalInfo, Is.InstanceOf<AggregateException>());
        Assert.That(result.Files.Count, Is.GreaterThan(0));
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_AllSucceed_ErrorIsNull()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = true;
        Options.ThrowErrorOnFailure = false;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*.txt";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Files.Count, Is.GreaterThan(0));
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_False_ThrowsOnFirstFailure()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = false;
        Options.ThrowErrorOnFailure = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PreserveDirectoryStructure = false;

        Assert.Throws<Exception>(() =>
            Smb.CopyFiles(Input, Connection, Options, CancellationToken.None));
    }
}
