using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Frends.Smb.MoveFiles.Definitions;
using NUnit.Framework;

namespace Frends.Smb.MoveFiles.Tests;

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
            .UntilCommandIsCompleted("samba-tool user list"))
            .Build();

        await adDcContainer.StartAsync();

        var (stdout, stderr) = await adDcContainer.GetLogsAsync();
        TestContext.WriteLine("=== CONTAINER LOGS ===");
        TestContext.WriteLine(stdout);
        TestContext.WriteLine(stderr);

        await Task.Delay(TimeSpan.FromSeconds(5));

        await adDcContainer.ExecAsync(["sh", "-c", "chmod 777 /share"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "printf '[testshare]\\n        path = /share\\n        writeable = Yes\\n        browseable = Yes\\n        force user = root\\n        create mask = 0777\\n        directory mask = 0777\\n' >> /usr/local/samba/etc/smb.conf"]);
        await adDcContainer.ExecAsync(["sh", "-c", $"samba-tool user create testuser {password}"]);
        await adDcContainer.ExecAsync(["sh", "-c", "samba-tool group addmembers 'Domain Admins' testuser"]);
        await adDcContainer.ExecAsync(["sh", "-c",
            "sed -i 's/\\[global\\]/[global]\\n\\tlog level = 3/' /usr/local/samba/etc/smb.conf"]);
        await adDcContainer.ExecAsync(["sh", "-c", "smbcontrol all reload-config"]);

        var confResult = await adDcContainer.ExecAsync(["sh", "-c", "cat /usr/local/samba/etc/smb.conf"]);
        TestContext.WriteLine("=== SMB.CONF ===");
        TestContext.WriteLine(confResult.Stdout);

        var userResult = await adDcContainer.ExecAsync(["sh", "-c", "samba-tool user list"]);
        TestContext.WriteLine("=== USERS ===");
        TestContext.WriteLine(userResult.Stdout);

        var groupResult = await adDcContainer.ExecAsync(["sh", "-c", "samba-tool group listmembers 'Domain Admins'"]);
        TestContext.WriteLine("=== DOMAIN ADMINS ===");
        TestContext.WriteLine(groupResult.Stdout);

        var spnResult = await adDcContainer.ExecAsync(["sh", "-c", "samba-tool spn list DC1$"]);
        TestContext.WriteLine("=== SPN ===");
        TestContext.WriteLine(spnResult.Stdout);
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (adDcContainer != null)
            await adDcContainer.DisposeAsync();

        Directory.Delete(testFilesPath, true);
    }

    [SetUp]
    public void Setup()
    {
        connection = new Connection
        {
            Server = "127.0.0.1",
            KerberosServerName = "DC1.test.local",
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
    public async Task MoveFiles_Kerberos_SingleFile_Success()
    {
        var confResult = await adDcContainer.ExecAsync(["sh", "-c", "cat /usr/local/samba/etc/smb.conf"]);
        TestContext.WriteLine("=== SMB.CONF ===");
        TestContext.WriteLine(confResult.Stdout);

        var userResult = await adDcContainer.ExecAsync(["sh", "-c", "samba-tool user list"]);
        TestContext.WriteLine("=== USERS ===");
        TestContext.WriteLine(userResult.Stdout);

        var groupResult = await adDcContainer.ExecAsync(["sh", "-c", "samba-tool group listmembers 'Domain Admins'"]);
        TestContext.WriteLine("=== DOMAIN ADMINS ===");
        TestContext.WriteLine(groupResult.Stdout);

        var findLog = await adDcContainer.ExecAsync(["sh", "-c", "find /usr/local/samba/var -name '*.log' 2>/dev/null"]);
        TestContext.WriteLine("=== LOG FILES ===");
        TestContext.WriteLine(findLog.Stdout);

        var findLog2 = await adDcContainer.ExecAsync(["sh", "-c", "find /var/log/samba -name '*.log' 2>/dev/null"]);
        TestContext.WriteLine("=== LOG FILES 2 ===");
        TestContext.WriteLine(findLog2.Stdout);

        await File.WriteAllTextAsync(Path.Combine(testFilesPath, "source", "single.txt"), "is Kerberos working?");
        input = new Input { SourcePath = "source/single.txt", TargetPath = "target" };

        var result = Smb.MoveFiles(input, connection, options, CancellationToken.None);

        var logResult = await adDcContainer.ExecAsync(["sh", "-c", "cat /usr/local/samba/var/log.smbd"]);
        TestContext.WriteLine("=== SAMBA LOG ===");
        TestContext.WriteLine(logResult.Stdout);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(testFilesPath, "target", "single.txt")), Is.True);
    }
}