using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.ReadFile.Definitions;
using NUnit.Framework;

namespace Frends.Smb.ReadFile.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g. WSL2).
// They will not run on Windows natively because SMB port 445 is reserved by the OS.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
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

    private IContainer sambaContainer;
    private string testFilesPath;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "test-files");
        Directory.CreateDirectory(testFilesPath);
        File.WriteAllText(Path.Combine(testFilesPath, "test-utf8.txt"), "Hello World");
        File.WriteAllText(Path.Combine(testFilesPath, "large-file.txt"), new string('x', 1024 * 1024));
        File.WriteAllText(Path.Combine(testFilesPath, "test-windows1252.txt"), "Test content with special chars");
        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-server-{Guid.NewGuid()}")
            .WithBindMount(testFilesPath, "/share")
            .WithPortBinding(445, 445)
            .WithCommand(
                "-u",
                "testuser;testpass",
                "-s",
                "testshare;/share;yes;no;no;testuser",
                "-w",
                "WORKGROUP")
            .Build();

        await sambaContainer.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
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
            UserName = userName,
            Password = password,
        };

        options = new Options
        {
            ThrowErrorOnFailure = false,
            ErrorMessageOnFailure = string.Empty,
            FileEncoding = FileEncoding.Utf8,
            EnableBom = false,
            EncodingInString = null,
        };
    }

    [Test]
    public async Task ReadFile_ValidUTF8File_Success()
    {
        input = new Input
        {
            Path = @"test-utf8.txt",
        };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        var contentString = Encoding.UTF8.GetString(result.Content);
        Assert.That(contentString, Does.Contain("Hello World"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ReadFile_DifferentEncodings_Success()
    {
        options.FileEncoding = FileEncoding.Windows1252;

        input = new Input { Path = @"test-windows1252.txt" };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        var contentString = Encoding.UTF8.GetString(result.Content);
        Assert.That(contentString, Does.Contain("special chars"));
    }

    [Test]
    public async Task ReadFile_LargeFile_ReadsInChunks()
    {
        input = new Input { Path = @"large-file.txt" };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.SizeInMegaBytes, Is.GreaterThan(0));
        Assert.That(result.Content, Is.Not.Null);
    }

    [Test]
    public async Task ReadFile_InvalidCredentials_LoginFailed()
    {
        connection = new Connection
        {
            Server = serverName,
            Share = shareName,
            UserName = "WORKGROUP\\wronguser",
            Password = "wrongpass",
        };

        input = new Input { Path = @"test-utf8.txt" };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Message, Does.Contain("STATUS_ACCESS_DENIED"));
    }

    [Test]
    public void ReadFile_EmptyPath_ThrowsArgumentException()
    {
        input = new Input { Path = string.Empty };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("Path cannot be empty"));
    }

    [Test]
    public void ReadFile_EmptyServer_ThrowsArgumentException()
    {
        connection.Server = string.Empty;
        input = new Input { Path = "test-utf8.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("Server cannot be empty"));
    }

    [Test]
    public void ReadFile_EmptyShare_ThrowsArgumentException()
    {
        connection.Share = string.Empty;
        input = new Input { Path = "test-utf8.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("Share cannot be empty"));
    }

    [Test]
    public void ReadFile_PathStartsWithUnc_ThrowsArgumentException()
    {
        input = new Input { Path = @"\\server\share\file.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("Path should be relative to the share"));
    }

    [Test]
    public async Task ReadFile_FileNotFound_ReturnsFailureResult()
    {
        input = new Input { Path = "nonexistent-file.txt" };

        var result = await Smb.ReadFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.Message, Does.Contain("Failed to open file"));
    }

    [Test]
    public void ReadFile_InvalidUsernameFormat_ThrowsArgumentException()
    {
        connection.UserName = "testuser";
        input = new Input { Path = @"test-utf8.txt" };
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex.Message, Does.Contain("UserName field must be of format domain\\username"));
    }

    [Test]
    public void ReadFile_CustomEncodingInvalid_ThrowsArgumentException()
    {
        input = new Input { Path = @"test-utf8.txt" };
        options.FileEncoding = FileEncoding.Other;
        options.EncodingInString = "invalid-encoding";
        options.ThrowErrorOnFailure = true;

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await Smb.ReadFile(input, connection, options, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
    }
}