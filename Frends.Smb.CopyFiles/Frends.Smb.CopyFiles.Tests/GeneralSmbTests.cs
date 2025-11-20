using System.Threading;
using NUnit.Framework;

namespace Frends.Smb.CopyFiles.Tests;

[TestFixture]
public class GeneralSmbTests : SmbTestBase
{
    [Test]
    public void InvalidCredentials_Fails()
    {
        Connection.Username = @"WORKGROUP\wrongUser";
        Connection.Password = "wrongPass";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain("STATUS_ACCESS_DENIED"));
    }

    [Test]
    public void EmptySourcePath_Fails()
    {
        Input.SourcePath = string.Empty;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain("Path cannot be empty"));
    }

    [Test]
    public void Accepts_ServerName_AsHostname()
    {
        Connection.Server = "localhost";
        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void EmptyServer_Fails()
    {
        Connection.Server = string.Empty;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain("Server cannot be empty"));
    }

    [Test]
    public void EmptyShare_Fails()
    {
        Connection.Share = string.Empty;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain("Share cannot be empty"));
    }

    [Test]
    public void PathStartsWithUnc_Fails()
    {
        Input.SourcePath = @"\\server\share\file.txt";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain("Path should be relative to the share"));
    }

    [Test]
    public void InvalidUsernameFormat_Fails()
    {
        Connection.Username = "user";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message, Does.Contain(@"Username field must be of format domain\username"));
    }
}
