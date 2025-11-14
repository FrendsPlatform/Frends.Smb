using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.CreateDirectory.Definitions;
using NUnit.Framework;

namespace Frends.Smb.CreateDirectory.Tests;

// These SMB integration tests require Docker and a Linux-compatible environment (e.g., WSL2).
// They will not run on Windows natively because the OS reserves SMB port 445.
// To execute the tests, run them inside WSL with Docker running:
//    dotnet test
// The tests will automatically start a temporary Samba container and mount test files for reading.
public abstract class SmbTestBase
{
    protected static readonly string TestDirPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "share");

    private const string ServerName = "127.0.0.1";
    private const string ShareName = "test-share";
    private const string Password = "pass";
    private const string User = @"WORKGROUP\user";

    private static readonly object Lock = new();

    private static IContainer sambaContainer;
    private static int refCount;
    private static bool isInitialized;

    protected Input Input { get; set; }

    protected Connection Connection { get; set; }

    protected Options Options { get; set; }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        bool shouldInitialize = false;
        lock (Lock)
        {
            refCount++;
            if (!isInitialized)
            {
                isInitialized = true;
                shouldInitialize = true;
            }
        }

        if (!shouldInitialize) return;

        Console.WriteLine($"Starting Samba container for tests in {TestDirPath}");

        Directory.CreateDirectory(Path.Combine(TestDirPath, "oldDir"));

        var container = new ContainerBuilder()
            .WithImage("dperson/samba:latest")
            .WithName($"smb-test-server-{Guid.NewGuid()}")
            .WithBindMount(TestDirPath, "/share")
            .WithPortBinding(445, 445)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(445))
            .WithCommand(
                "-n",
                "-u",
                "user;pass",
                "-s",
                "test-share;/share;no;no;no;user,root",
                "-w",
                "WORKGROUP")
            .Build();

        await container.StartAsync();
        await container.ExecAsync(["chmod", "-R", "777", "/share"]);

        lock (Lock)
        {
            sambaContainer = container;
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        bool shouldCleanup = false;

        lock (Lock)
        {
            refCount--;
            if (refCount <= 0)
            {
                shouldCleanup = true;
                isInitialized = false;
            }
        }

        if (!shouldCleanup) return;

        if (sambaContainer != null)
        {
            Console.WriteLine("Stopping Samba container");

            await sambaContainer.DisposeAsync();
            sambaContainer = null;
        }

        if (Directory.Exists(TestDirPath))
        {
            Directory.Delete(TestDirPath, true);
        }
    }

    [SetUp]
    public void Setup()
    {
        Console.WriteLine($"Starting test for {GetType().Name}");
        Connection = new Connection { Server = ServerName, Share = ShareName, Username = User, Password = Password };
        Options = new Options { ThrowErrorOnFailure = false, ErrorMessageOnFailure = string.Empty };
        Input = new Input { DirectoryPath = "testDir" };
    }

    [TearDown]
    public void TearDown()
    {
        Console.WriteLine($"Cleaning after test {GetType().Name}");

        if (!Directory.Exists(TestDirPath)) return;
        var children = Directory.GetFileSystemEntries(TestDirPath);
        foreach (var child in children)
            Directory.Delete(child, true);
    }
}
