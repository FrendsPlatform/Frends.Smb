using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Frends.Smb.CopyFiles.Tests;

[TestFixture]
public class CopyFilesTests : SmbTestBase
{
    [Test]
    public void SimpleCopyFiles()
    {
        Input.SourcePath = "src/testFile.txt";
        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "testFile.txt")), Is.True);
    }
}
