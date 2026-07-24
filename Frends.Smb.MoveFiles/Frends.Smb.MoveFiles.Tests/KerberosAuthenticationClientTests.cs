using System;
using System.Threading;
using Frends.Smb.MoveFiles.Helpers;
using Kerberos.NET.Server;
using NUnit.Framework;

namespace Frends.Smb.MoveFiles.Tests;

[TestFixture]
public class KerberosAuthenticationClientTests
{
    private const string Realm = "TEST.LOCAL";
    private const string Username = "testuser";
    private const string Password = "Passw0rd123!";
    private const string Server = "fakeserver.test.local";
    private const int KdcPort = 18888;

    private KdcServiceListener kdc;

    [OneTimeSetUp]
    public void StartKdc()
    {
        var options = new ListenerOptions
        {
            DefaultRealm = Realm,
            IsDebug = true,
            RealmLocator = realm => new FakeRealmService(realm, Password),
        };

        options.Configuration.KdcDefaults.KdcTcpListenEndpoints.Clear();
        options.Configuration.KdcDefaults.KdcTcpListenEndpoints.Add($"127.0.0.1:{KdcPort}");

        kdc = new KdcServiceListener(options);

        _ = kdc.Start();

        Thread.Sleep(500);
    }

    [OneTimeTearDown]
    public void StopKdc()
    {
        kdc?.Dispose();
    }

    [Test]
    public void InitializeSecurityContext_ReturnsToken_ForValidCredentials()
    {
        var authClient = new KerberosNetAuthenticationClient(
            domain: Realm,
            username: Username,
            password: Password,
            server: Server,
            kdcAddress: $"127.0.0.1:{KdcPort}");

        byte[] token = authClient.InitializeSecurityContext(inputToken: null);

        Assert.That(token, Is.Not.Null.And.Not.Empty);
        Assert.That(authClient.GetSessionKey(), Is.Not.Null);
    }

    [Test]
    public void InitializeSecurityContext_Throws_ForWrongPassword()
    {
        var authClient = new KerberosNetAuthenticationClient(
            domain: Realm,
            username: Username,
            password: "InCorrect!",
            server: Server,
            kdcAddress: $"127.0.0.1:{KdcPort}");

        Assert.Throws<AggregateException>(
            () => authClient.InitializeSecurityContext(inputToken: null));
    }
}