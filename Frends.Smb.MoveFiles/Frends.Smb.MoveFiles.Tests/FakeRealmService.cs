using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Kerberos.NET.Configuration;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Kerberos.NET.Entities.Pac;
using Kerberos.NET.Server;

namespace Frends.Smb.MoveFiles.Tests;

internal class FakeRealmService : IRealmService
{
    private readonly string _password;

    public FakeRealmService(string realm, string password)
    {
        Name = realm;
        _password = password;
    }

    public string Name { get; }

    public IRealmSettings Settings => new FakeRealmSettings();

    public IPrincipalService Principals => new FakePrincipalService(Name, _password);

    public ITrustedRealmService TrustedRealms => null;

    public Krb5Config Configuration => Krb5Config.Default();

    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
}

internal class FakePrincipalService : IPrincipalService
{
    private readonly string _realm;
    private readonly string _password;

    public FakePrincipalService(string realm, string password)
    {
        _realm = realm;
        _password = password;
    }

    public IKerberosPrincipal Find(KrbPrincipalName principalName, string realm = null)
        => new FakeKerberosPrincipal(principalName.FullyQualifiedName, _realm, _password);

    public Task<IKerberosPrincipal> FindAsync(KrbPrincipalName principalName, string realm = null)
        => Task.FromResult(Find(principalName, realm));

    public X509Certificate2 RetrieveKdcCertificate() => null;

    public IExchangeKey RetrieveKeyCache(KeyAgreementAlgorithm algorithm) => null;

    public IExchangeKey CacheKey(IExchangeKey key) => key;
}

internal class FakeKerberosPrincipal : IKerberosPrincipal
{
    private static readonly byte[] KrbTgtKey = new byte[16];
    private static readonly ConcurrentDictionary<string, KerberosKey> KeyCache = new();
    private readonly string _realm;
    private readonly byte[] _passwordBytes;

    public FakeKerberosPrincipal(string principalName, string realm, string password)
    {
        PrincipalName = principalName;
        _realm = realm;
        _passwordBytes = Encoding.Unicode.GetBytes(password);
        Expires = DateTimeOffset.UtcNow.AddYears(10);
    }

    public string PrincipalName { get; }

    public DateTimeOffset? Expires { get; }

    public PrincipalType Type => PrincipalType.User;

    public SupportedEncryptionTypes SupportedEncryptionTypes =>
        SupportedEncryptionTypes.Aes256CtsHmacSha196 |
        SupportedEncryptionTypes.Aes128CtsHmacSha196 |
        SupportedEncryptionTypes.Rc4Hmac;

    public IEnumerable<PaDataType> SupportedPreAuthenticationTypes =>
        new[] { PaDataType.PA_ENC_TIMESTAMP };

    public void Validate(X509Certificate2Collection certificates)
    {
    }

    public KerberosKey RetrieveLongTermCredential()
        => RetrieveLongTermCredential(EncryptionType.AES256_CTS_HMAC_SHA1_96);

    public KerberosKey RetrieveLongTermCredential(EncryptionType etype)
    {
        if (PrincipalName == "krbtgt" || PrincipalName.StartsWith("krbtgt/"))
        {
            return new KerberosKey(
                password: KrbTgtKey,
                principal: new PrincipalName(PrincipalNameType.NT_PRINCIPAL, _realm, new[] { "krbtgt" }),
                etype: EncryptionType.AES256_CTS_HMAC_SHA1_96,
                saltType: SaltType.ActiveDirectoryUser);
        }

        return KeyCache.GetOrAdd($"{PrincipalName}:{etype}", _ => new KerberosKey(
            password: _passwordBytes,
            principal: new PrincipalName(PrincipalNameType.NT_PRINCIPAL, _realm, new[] { PrincipalName }),
            etype: etype,
            saltType: SaltType.ActiveDirectoryUser));
    }

    public PrivilegedAttributeCertificate GeneratePac() => new()
    {
        LogonInfo = new PacLogonInfo
        {
            DomainName = _realm,
            UserName = PrincipalName,
            UserDisplayName = PrincipalName,
            LogonTime = DateTimeOffset.UtcNow,
            ServerName = "fakeserver",
            UserAccountControl = UserAccountControlFlags.ADS_UF_NORMAL_ACCOUNT,
        },
    };
}

internal class FakeRealmSettings : IRealmSettings
{
    public TimeSpan MaximumSkew => TimeSpan.FromMinutes(5);

    public TimeSpan SessionLifetime => TimeSpan.FromHours(10);

    public TimeSpan MaximumRenewalWindow => TimeSpan.FromDays(7);

    public KerberosCompatibilityFlags Compatibility => KerberosCompatibilityFlags.None;
}