using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.ReadFile.Definitions;
using NUnit.Framework;

namespace Frends.Smb.ReadFile.Tests;

// The SMB tests require Linux or WSL with Docker installed.
// These tests will not work on Windows natively because SMB uses port 445, which is reserved by the OS.
// `cd Frends.Smb.ReadFile.Tests`
// `docker-compose up -d`
// `dotnet test`

[TestFixture]
public class UnitTests
{
    private readonly string serverName = "127.0.0.1";
    private readonly string shareName = "testshare";
    private readonly string userName = "WORKGROUP\\testuser";
    private readonly string password = "testpass";

    private Input input;
    private Connection connection;
    private Options options;

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            UserName = userName,
            Password = password,
        };

        options = new Options
        {
            ThrowErrorOnFailure = false,
            ErrorMessageOnFailure = string.Empty,
            FileEncoding = FileEncoding.UTF8,
            EnableBom = false,
            EncodingInString = null,
        };
    }

    [Test]
    public async Task ReadFile_ValidUTF8File_Success()
    {
        input = new Input
        {
            Path = $"\\\\{serverName}\\{shareName}\\test-utf8.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Does.Contain("Hello World"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ReadFile_InvalidPath_ThrowsException()
    {
        input = new Input
        {
            Path = $"\\\\{serverName}\\{shareName}\\nonexistent-file.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("Failed to open file"));
    }

    [Test]
    public async Task ReadFile_InvalidCredentials_LoginFailed()
    {
        connection = new Connection
        {
            UserName = "WORKGROUP\\wronguser",
            Password = "wrongpass",
        };

        input = new Input
        {
            Path = $"\\\\{serverName}\\{shareName}\\test-utf8.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("Failed to connect"));
    }

    [Test]
    public async Task ReadFile_DifferentEncodings_Success()
    {
        options.FileEncoding = FileEncoding.Windows1252;

        input = new Input
        {
            Path = $"\\\\{serverName}\\{shareName}\\test-windows1252.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Is.Not.Null);
    }

    [Test]
    public async Task ReadFile_LargeFile_ReadsInChunks()
    {
        input = new Input
        {
            Path = $"\\\\{serverName}\\{shareName}\\large-file.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.SizeInMegaBytes, Is.GreaterThan(0));
        Assert.That(result.Content, Is.Not.Null);
    }
}