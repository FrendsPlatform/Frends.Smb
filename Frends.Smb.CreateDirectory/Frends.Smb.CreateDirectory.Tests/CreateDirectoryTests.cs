using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Frends.Smb.CreateDirectory.Tests;

[TestFixture]
public class CreateDirectoryTests : SmbTestBase
{
    [Test]
    public void SimpleCreateDirectory()
    {
        const string newDir = "newDir";
        Input.DirectoryPath = newDir;
        var result = Smb.CreateDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, newDir)), Is.True);
    }

    [Test]
    public void CreateAlreadyExistingDirectory()
    {
        const string newDir = "oldDir";
        Input.DirectoryPath = newDir;
        var result = Smb.CreateDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, newDir)), Is.True);
    }

    [Test]
    public void CreateDirectoryWithSubDirectories()
    {
        var newDir = Path.Combine("newDir", "subDir");
        Input.DirectoryPath = newDir;
        var result = Smb.CreateDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, newDir)), Is.True);
    }

    [Test]
    public void CreateDirectoryInPreExistedDirectory()
    {
        var newDir = Path.Combine("oldDir", "subDir");
        Input.DirectoryPath = newDir;
        var result = Smb.CreateDirectory(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.Exists(Path.Combine(TestDirPath, newDir)), Is.True);
    }
}
