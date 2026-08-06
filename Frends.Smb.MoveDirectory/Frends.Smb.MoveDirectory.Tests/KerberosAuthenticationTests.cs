using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Frends.Smb.MoveDirectory.Definitions;
using NUnit.Framework;

namespace Frends.Smb.MoveDirectory.Tests;

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

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        testFilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, $"test-files-kerberos-{Guid.NewGuid()}");
        Directory.CreateDirectory(testFilesPath);
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "target"));

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
            "sed -i '/\\[global\\]/a\\        server signing = auto\\n        server smb encrypt = off' /usr/local/samba/etc/smb.conf"]);

        var hostsProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = "bash -c \"echo '127.0.0.1 DC1.test.local DC1' >> /etc/hosts\"",
            UseShellExecute = false,
        });
        await hostsProcess!.WaitForExitAsync();

        await adDcContainer.ExecAsync(["sh", "-c", "smbcontrol all reload-config"]);
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (adDcContainer != null)
            await adDcContainer.DisposeAsync();

        if (Directory.Exists(testFilesPath))
        {
            Directory.Delete(testFilesPath, true);
        }
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
            IfTargetDirectoryExists = DirectoryExistsAction.Throw,
        };
    }

    [TearDown]
    public void Cleanup()
    {
        if (!Directory.Exists(testFilesPath)) return;
        foreach (var entry in Directory.GetFileSystemEntries(testFilesPath))
            Directory.Delete(entry, true);

        Directory.CreateDirectory(Path.Combine(testFilesPath, "source"));
        Directory.CreateDirectory(Path.Combine(testFilesPath, "target"));
    }

    [Test]
    public void MoveDirectory_Kerberos_Success()
    {
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source", "test-dir"));
        input = new Input { SourcePath = "source/test-dir", TargetPath = "target/moved-dir" };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "source", "test-dir")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(testFilesPath, "target", "moved-dir")), Is.True);
    }

    [Test]
    public void MoveDirectory_Kerberos_WrongPassword_Fails()
    {
        connection.Password = "WrongPassword123!";
        options.ThrowErrorOnFailure = false;
        Directory.CreateDirectory(Path.Combine(testFilesPath, "source", "test-dir"));
        input = new Input { SourcePath = "source/test-dir", TargetPath = "target/moved-dir" };

        var result = Smb.MoveDirectory(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }
}