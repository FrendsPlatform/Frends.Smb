using System.IO;
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

        Assert.That(result.Success, Is.True);
        Assert.That(result.Files.Count, Is.EqualTo(expectedCopiesCount));
        var srcPat = Path.Combine(TestDirPath, sourcePath);
        if (File.Exists(srcPat))
        {
            var filesCount = Directory.GetFiles(Path.Combine(TestDirPath, "dst", "src")).Length;
            Assert.That(filesCount, Is.EqualTo(expectedCopiesCount));
        }
        else
        {
            var filesCount = Directory.GetFiles(Path.Combine(TestDirPath, "dst", "src", "src")).Length;
            Assert.That(filesCount, Is.EqualTo(expectedCopiesCount));
        }
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
        Assert.That(result.Success, Is.True);
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
        Assert.That(result.Error.Message, Contains.Substring(@"File dst\old.foo already exists."));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
    }

    [Test]
    public void RollbackTmpFilesWhenErrorOccured()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        Input.SourcePath = "src/error";
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
        Input.SourcePath = "src/error";
        Options.CreateTargetDirectories = false;
        Options.Recursive = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        Assert.That(result.Success, Is.True);

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
        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old.foo")), Is.EqualTo("new test content"));
    }

    [Test]
    public void FileExistsActionIsRename()
    {
        Options.IfTargetFileExists = FileExistsAction.Rename;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        Assert.That(result.Success, Is.True);

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
        Assert.That(result.Success, Is.True);

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

        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.False);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }

    [Test]
    public void CopyRecursiveIsTrue()
    {
        Options.Recursive = true;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        Assert.That(result.Success, Is.True);

        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.True);
    }

    [Test]
    public void CopyRecursiveIsFalse()
    {
        Options.Recursive = false;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        Assert.That(result.Error.Message, Is.Empty);
        Assert.That(result.Success, Is.True);

        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }
}
