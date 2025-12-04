using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frends.Smb.CopyFiles.Definitions;
using NUnit.Framework;

namespace Frends.Smb.CopyFiles.Tests;

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

    protected Input Input { get; private set; }

    protected Connection Connection { get; private set; }

    protected Options Options { get; private set; }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        bool shouldInitialize = false;
        lock (Lock)
        {
            refCount++;
            if (!isInitialized && refCount == 1) shouldInitialize = true;
            if (!isInitialized && refCount > 1)
            {
                while (!isInitialized)
                    Monitor.Wait(Lock);
            }
        }

        if (shouldInitialize)
        {
            await PrepareSrcDirectory();

            sambaContainer = new ContainerBuilder()
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
                    "test-share;/share;yes;no;yes;all;root",
                    "-w",
                    "WORKGROUP",
                    "-p")
                .Build();

            await sambaContainer.StartAsync();

            lock (Lock)
            {
                isInitialized = true;
                Monitor.PulseAll(Lock);
            }
        }

        if (sambaContainer is not null)
        {
            await sambaContainer.ExecAsync(["chmod", "-R", "777", "/share"]);
            await sambaContainer.ExecAsync(["chmod", "-R", "g+s", "/share"]);
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
            }
        }

        if (!shouldCleanup) return;

        if (sambaContainer != null)
        {
            await sambaContainer.DisposeAsync();
            lock (Lock)
            {
                isInitialized = false;
                sambaContainer = null;
            }
        }
    }

    [SetUp]
    public async Task Setup()
    {
        await sambaContainer.ExecAsync(["chmod", "-R", "777", "/share"]);
        Connection = new Connection { Server = ServerName, Share = ShareName, Username = User, Password = Password };
        Options = new Options { ThrowErrorOnFailure = false, ErrorMessageOnFailure = string.Empty };
        Input = new Input { SourcePath = string.Empty, TargetPath = "dst" };
        await PrepareDstDirectory();
    }

    private static async Task PrepareDstDirectory()
    {
        var dstPath = Path.Join(TestDirPath, "dst");
        if (sambaContainer is not null)
            await sambaContainer.ExecAsync(["chmod", "-R", "777", "/share"]);

        if (Directory.Exists(dstPath)) Directory.Delete(Path.Join(TestDirPath, "dst"), true);

        Directory.CreateDirectory(Path.Combine(dstPath, "oldSub"));
        await File.WriteAllTextAsync(Path.Join(dstPath, "old.foo"), "old test content");

        Directory.CreateDirectory(Path.Combine(dstPath, "error"));
        await File.WriteAllTextAsync(Path.Join(dstPath, "error", "old.foo"), "old test content");
    }

    private static async Task PrepareSrcDirectory()
    {
        var srcPath = Path.Join(TestDirPath, "src");
        Directory.CreateDirectory(Path.Combine(TestDirPath, "src"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "test1.txt"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "test2.txt"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "test3.txt"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "test4.txt"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "the-test.txt"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "test1.log"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "test2.log"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "fest.log"), "test content");
        await File.WriteAllTextAsync(Path.Join(srcPath, "old.foo"), "new test content");

        Directory.CreateDirectory(Path.Combine(srcPath, "subDir"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "subDir", "sub.foo"), "test content");

        Directory.CreateDirectory(Path.Combine(srcPath, "subDir", "subSubDir"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "subDir", "subSubDir", "sub.foo"), "test content");

        Directory.CreateDirectory(Path.Combine(srcPath, "oldSub"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "oldSub", "oldSub1.foo"), "test content");

        Directory.CreateDirectory(Path.Combine(srcPath, "error"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "error", "old.foo"), "test content");
        Directory.CreateDirectory(Path.Combine(srcPath, "error", "errorSubDir"));
        await File.WriteAllTextAsync(Path.Join(srcPath, "error", "errorSubDir", "old.foo"), "test content");
    }
}
