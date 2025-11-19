using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Frends.Smb.DeleteDirectory.Tests;

[TestFixture]
public class CreateDirectoryTests : SmbTestBase
{
    [Test]
    public void SimpleDeleteDirectory()
    {
        var dirPath = Path.Combine("oldDir", "subDir");
        Input.DirectoryPath = dirPath;
        var result = Smb.DeleteDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, dirPath)), Is.False);
    }

    [Test]
    public void DeleteDirectoryWhenNotExists()
    {
        const string dirPath = "notExistent";
        Input.DirectoryPath = dirPath;
        var result = Smb.DeleteDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, dirPath)), Is.False);
    }

    [Test]
    public void DeleteDirectoryWithSubDirectoriesRecursive()
    {
        const string dirPath = "oldDir";
        Input.DirectoryPath = dirPath;
        Options.DeleteRecursively = true;
        var result = Smb.DeleteDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, dirPath)), Is.False);
    }

    [Test]
    public void FailedDeleteDirectoryWithSubDirectoriesNonRecursive()
    {
        const string dirPath = "oldDir";
        Input.DirectoryPath = dirPath;
        Options.DeleteRecursively = false;
        var result = Smb.DeleteDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, dirPath)), Is.True);
    }
}
