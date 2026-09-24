using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Frends.Smb.CopyFiles.Definitions;
using Frends.Smb.CopyFiles.Helpers;
using NUnit.Framework;

namespace Frends.Smb.CopyFiles.Tests;

[NonParallelizable]
[TestFixture]
public class KerberosAuthenticationTests
{
    private const string Realm = "TEST.LOCAL";
    private const string DcHostname = "DC1";
    private readonly string shareName = "testshare";
    private readonly string user = "TEST.LOCAL\\testuser";
    private readonly string password = "Passw0rd123!";
    private Input input;
    private Connection connection;
    private Options options;
    private DotNet.Testcontainers.Containers.IContainer adDcContainer;
    private string testFilesPath;
    private string kerberosCacheDirectory;
    private string kerberosCacheHostPath;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, $"test-files-kerberos-{Guid.NewGuid()}");
        Directory.CreateDirectory(testFilesPath);
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "target"));

        // Kept outside testFilesPath so the per-test Cleanup() (which recursively deletes
        // everything under testFilesPath) does not remove the ccache file between tests.
        kerberosCacheDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, $"kcache-kerberos-{Guid.NewGuid()}");
        Directory.CreateDirectory(kerberosCacheDirectory);
        kerberosCacheHostPath = Path.Combine(kerberosCacheDirectory, "krb5cc_testuser");

        adDcContainer = new ContainerBuilder()
            .WithImage("diegogslomp/samba-ad-dc:latest")
            .WithName($"smb-test-kerberos-{Guid.NewGuid()}")
            .WithHostname(DcHostname)
            .WithPrivileged(true)
            .WithEnvironment("REALM", Realm)
            .WithEnvironment("DOMAIN", "TEST")
            .WithEnvironment("ADMIN_PASS", password)
            .WithEnvironment("DNS_FORWARDER", "8.8.8.8")
            .WithBindMount(testFilesPath, "/share")
            .WithBindMount(kerberosCacheDirectory, "/kcache")
            .WithCreateParameterModifier(p => p.HostConfig.NetworkMode = "host")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted("samba-tool user list")
                .UntilCommandIsCompleted("smbclient -L localhost -U% -N"))
            .Build();

        await adDcContainer.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(30));

        await adDcContainer.ExecAsync(["sh", "-c", "chmod 777 /share"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "printf '[testshare]\\n        path = /share\\n        writeable = Yes\\n        browseable = Yes\\n        force user = root\\n        create mask = 0777\\n        directory mask = 0777\\n' >> /usr/local/samba/etc/smb.conf"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            $"samba-tool user create testuser {password} --uid-number=10001 --login-shell=/bin/bash --unix-home=/home/testuser"]);
        await adDcContainer.ExecAsync(["sh", "-c", "samba-tool group addmembers 'Domain Admins' testuser"]);
        await adDcContainer.ExecAsync(["sh", "-c", "mkdir -p /home/testuser && chmod 755 /home/testuser"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "sed -i '/bind interfaces only/d' /usr/local/samba/etc/smb.conf"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "sed -i '/interfaces = lo eth0/d' /usr/local/samba/etc/smb.conf"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "sed -i '/\\[global\\]/a\\        server signing = mandatory\\n        server smb encrypt = off' /usr/local/samba/etc/smb.conf"

        var hostsProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = "bash -c \"echo '127.0.0.1 DC1.test.local DC1' >> /etc/hosts\"",
            UseShellExecute = false,
        });
        await hostsProcess!.WaitForExitAsync();

        await adDcContainer.ExecAsync(["sh", "-c", "smbcontrol all reload-config"]);

        var kinitResult = await adDcContainer.ExecAsync(["sh", "-c",
            $"echo '{password}' | KRB5CCNAME=/kcache/krb5cc_testuser kinit testuser@{Realm} && chmod 666 /kcache/krb5cc_testuser && klist -c /kcache/krb5cc_testuser"]);

        if (kinitResult.ExitCode != 0)
        {
            throw new Exception(
                $"kinit failed with exit code {kinitResult.ExitCode}.{Environment.NewLine}" +
                $"Stdout: {kinitResult.Stdout}{Environment.NewLine}Stderr: {kinitResult.Stderr}");
        }

        Console.Error.WriteLine($"kinit succeeded. Stdout: {kinitResult.Stdout}");
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (adDcContainer != null)
            await adDcContainer.DisposeAsync();

        Directory.Delete(testFilesPath, true);

        if (Directory.Exists(kerberosCacheDirectory))
            Directory.Delete(kerberosCacheDirectory, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            Server = "127.0.0.1",
            KerberosServerName = "DC1.test.local",
            KdcAddress = "127.0.0.1:88",
            Share = shareName,
            Username = user,
            Password = password,
            AuthenticationMode = AuthenticationMode.Kerberos,
        };
        options = new Options
        {
            ThrowErrorOnFailure = true,
            ErrorMessageOnFailure = string.Empty,
            CreateTargetDirectories = true,
            IfTargetFileExists = FileExistsAction.Throw,
            PreserveDirectoryStructure = false,
        };
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(testFilesPath, "*", SearchOption.AllDirectories))
            File.Delete(file);
    }

    [Test]
    public async Task CopyFiles_Kerberos_SingleFile_Success()
    {
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "single.txt"), "is Kerberos working?");
        input = new Input { SourcePath = "source/single.txt", TargetPath = "target" };

        var result = Smb.CopyFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "single.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "single.txt")), Is.True);
    }

    [Test]
    public async Task CopyFiles_KerberosTicketCache_SingleFile_Success()
    {
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "single.txt"), "is Kerberos ticket cache working?");
        input = new Input { SourcePath = "source/single.txt", TargetPath = "target" };
        connection.AuthenticationMode = AuthenticationMode.KerberosTicketCache;
        connection.KerberosCacheFile = kerberosCacheHostPath;

        var result = Smb.CopyFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "single.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "source", "single.txt")), Is.True);
    }

    [Test]
    public async Task CopyFiles_KerberosTicketCache_MissingCacheFile_Fails()
    {
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "single.txt"), "is Kerberos ticket cache working?");
        input = new Input { SourcePath = "source/single.txt", TargetPath = "target" };
        connection.AuthenticationMode = AuthenticationMode.KerberosTicketCache;
        connection.KerberosCacheFile = Path.Combine(kerberosCacheDirectory, "nonexistent_krb5cc");
        options.ThrowErrorOnFailure = false;

        var result = Smb.CopyFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task CopyFiles_Kerberos_WrongPassword_Fails()
    {
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "single.txt"), "is Kerberos working?");
        connection.Password = "WrongPassword123!";
        input = new Input { SourcePath = "source/single.txt", TargetPath = "target" };
        options.ThrowErrorOnFailure = false;

        var result = Smb.CopyFiles(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task Debug_SessionKeyLength()
    {
        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "debug.txt"), "debug");
        input = new Input { SourcePath = "source/debug.txt", TargetPath = "target" };

        using var authClient = new KerberosNetAuthenticationClient(
            domain: "TEST.LOCAL",
            username: "testuser",
            password: "Passw0rd123!",
            server: "DC1.test.local",
            kdcAddress: "127.0.0.1:88");

        authClient.InitializeSecurityContext(null);

        Console.Error.WriteLine($"Session key length (raw): {authClient.SessionKeyLength} bytes");
        Console.Error.WriteLine($"Signing key length (after truncation): {authClient.SigningKeyLength} bytes");
        Assert.Pass($"Raw: {authClient.SessionKeyLength}, Signing: {authClient.SigningKeyLength}");
    }
}