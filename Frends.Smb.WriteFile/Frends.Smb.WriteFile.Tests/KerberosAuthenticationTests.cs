using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Frends.Smb.WriteFile.Definitions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Frends.Smb.WriteFile.Tests;

[NonParallelizable]
[TestFixture]
public class KerberosAuthenticationTests
{
    private const string Realm = "TEST.LOCAL";
    private const string DcHostname = "DC1";
    private const string TestFile = "test-utf8.txt";
    private static readonly byte[] SimpleContent = "Hello world"u8.ToArray();
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
            Directory.Delete(testFilesPath, true);
    }

    [TearDown]
    public void TearDown()
    {
        var children = Directory.GetFileSystemEntries(testFilesPath);

        foreach (var child in children)
        {
            var attr = File.GetAttributes(child);

            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                Directory.Delete(child, true);
            else
                File.Delete(child);
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
            ThrowErrorOnFailure = false,
            ErrorMessageOnFailure = string.Empty,
            Overwrite = false,
        };

        input = new Input
        {
            DestinationPath = TestFile,
            Content = SimpleContent,
        };
    }

    [Test]
    public void WriteFile_Kerberos_Success()
    {
        var result = Smb.WriteFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        var bytes = File.ReadAllBytes(Path.Combine(testFilesPath, TestFile));
        Assert.That(bytes, Is.EquivalentTo(SimpleContent));
    }

    [Test]
    public void WriteFile_Kerberos_WrongPassword_Fails()
    {
        connection.Password = "WrongPassword123!";

        var result = Smb.WriteFile(input, connection, options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
    }
}