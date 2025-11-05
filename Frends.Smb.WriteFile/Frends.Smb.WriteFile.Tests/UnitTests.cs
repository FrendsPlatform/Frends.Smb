using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.WriteFile.Definitions;
using NUnit.Framework;

namespace Frends.Smb.WriteFile.Tests;

[TestFixture]
public class UnitTests
{
    private const string ServerName = "127.0.0.1";
    private const string ShareName = "test-share";
    private const string Password = "pass";
    private const string User = @"WORKGROUP\user";

    private const string TestFile = "test-utf8.txt";
    private static readonly string DestinationDirPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "dst");
    private static readonly string DestinationFilePath = Path.Combine(DestinationDirPath, TestFile);

    private static readonly byte[] SimpleContent = "Hello world"u8.ToArray();

    // private static readonly byte[] LargeContent = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
    private Input input;
    private Connection connection;
    private Options options;

    private IContainer sambaContainer;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        Directory.CreateDirectory(DestinationDirPath);

        sambaContainer = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-server-{Guid.NewGuid()}")
            .WithBindMount(DestinationDirPath, "/share")
            .WithPortBinding(445, 445)
            .WithCommand(
                "-n",
                "-u",
                "user;pass",
                "-s",
                "test-share;/share;no;no;no;user",
                "-w",
                "WORKGROUP")
            .Build();

        await sambaContainer.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));

        var result = await sambaContainer.ExecAsync([
            "sh", "-c", "touch /share/debug.txt && echo 'ok'"
        ]);
        Console.WriteLine(result.Stdout + result.Stderr);
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (sambaContainer != null)
        {
            await sambaContainer.DisposeAsync();
        }

        Directory.Delete(DestinationDirPath, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection { Server = ServerName, Share = ShareName, Username = User, Password = Password };

        options = new Options { ThrowErrorOnFailure = false, ErrorMessageOnFailure = string.Empty, Overwrite = false };

        input = new Input { DestinationPath = TestFile, Content = SimpleContent };
    }

    [TearDown]
    public void TearDown()
    {
        var children = Directory.GetFileSystemEntries(DestinationDirPath);
        foreach (var child in children)
        {
            FileAttributes attr = File.GetAttributes(child);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
            {
                Directory.Delete(child, true);
            }
            else
            {
                File.Delete(child);
            }
        }
    }

    [Test]
    public void WriteSimpleFile()
    {
        var result = Smb.WriteFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Error?.Message, Is.Null);
        Assert.That(result.Success, Is.True);
        var bytes = File.ReadAllBytes(DestinationFilePath);
        Assert.That(bytes, Is.EquivalentTo(SimpleContent));
    }

    // [Test]
    // public void WriteLargeFile()
    // {
    //     input.Content = LargeContent;
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Error.Message, Is.Null);
    //     Assert.That(result.Success, Is.True);
    //     var bytes = File.ReadAllBytes(DestinationFilePath);
    //     Assert.That(bytes, Is.EquivalentTo(LargeContent));
    // }
    //
    // [Test]
    // public void OverwriteFile()
    // {
    //     File.WriteAllText(DestinationFilePath, "test");
    //     options.Overwrite = true;
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Error.Message, Is.Null);
    //     Assert.That(result.Success, Is.True);
    //     var res = File.ReadAllBytes(DestinationFilePath);
    //     Assert.That(res, Is.EquivalentTo(SimpleContent));
    // }
    //
    // [Test]
    // public void NoOverwriteFile()
    // {
    //     File.WriteAllText(DestinationFilePath, "test");
    //     options.Overwrite = false;
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error, Is.Not.Null);
    //     Assert.That(result.Error.Message, Does.Contain("STATUS_OBJECT_NAME_COLLISION"));
    // }
    //
    // [Test]
    // public void WriteFileInSubdirectory()
    // {
    //     input.DestinationPath = Path.Combine("subDir", TestFile);
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Error.Message, Is.Null);
    //     Assert.That(result.Success, Is.True);
    //     var dstFile = Path.Combine(DestinationDirPath, "subDir", TestFile);
    //     var bytes = File.ReadAllBytes(dstFile);
    //     Assert.That(bytes, Is.EquivalentTo(SimpleContent));
    // }
    //
    // [Test]
    // public void ResultReturnsCorrectSizeInMegaBytes()
    // {
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Error.Message, Is.Null);
    //     Assert.That(result.SizeInMegaBytes, Is.EqualTo(1));
    // }
    //
    // [Test]
    // public void WriteFile_InvalidCredentials_Fails()
    // {
    //     connection.Username = @"WORKGROUP\wrongUser";
    //     connection.Password = "wrongPass";
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain("STATUS_ACCESS_DENIED"));
    // }
    //
    // [Test]
    // public void WriteFile_EmptyPath_Fails()
    // {
    //     input = new Input { DestinationPath = string.Empty };
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain("Path cannot be empty"));
    // }
    //
    // [Test]
    // public void WriteFile_Accepts_ServerName_AsHostname()
    // {
    //     connection.Server = "localhost";
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //     Assert.That(result.Error.Message, Is.Null);
    //     Assert.That(result.Success, Is.True);
    // }
    //
    // [Test]
    // public void WriteFile_EmptyServer_Fails()
    // {
    //     connection.Server = string.Empty;
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain("Server cannot be empty"));
    // }
    //
    // [Test]
    // public void WriteFile_EmptyShare_Fails()
    // {
    //     connection.Share = string.Empty;
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain("Share cannot be empty"));
    // }
    //
    // [Test]
    // public void WriteFile_PathStartsWithUnc_Fails()
    // {
    //     input = new Input { DestinationPath = @"\\server\share\file.txt" };
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain("Path should be relative to the share"));
    // }
    //
    // [Test]
    // public void WriteFile_InvalidUsernameFormat_Fails()
    // {
    //     connection.Username = "user";
    //
    //     var result = Smb.WriteFile(input, connection, options, CancellationToken.None);
    //
    //     Assert.That(result.Success, Is.False);
    //     Assert.That(result.Error.Message, Does.Contain(@"UserName field must be of format domain\username"));
    // }
}
