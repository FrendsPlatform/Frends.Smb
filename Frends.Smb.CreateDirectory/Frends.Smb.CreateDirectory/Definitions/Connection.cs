using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.CreateDirectory.Definitions;

/// <summary>
/// Connection parameters.
/// </summary>
public class Connection
{
    /// <summary>
    /// SMB server address or hostname.
    /// </summary>
    /// <example>127.0.0.1</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Server { get; set; }

    /// <summary>
    /// SMB share name to connect to.
    /// </summary>
    /// <example>testShare</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Share { get; set; }

    /// <summary>
    /// Username for SMB authentication.
    /// This needs to be of format domain\username
    /// </summary>
    /// <example>WORKGROUP\Administrator</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Username { get; set; }

    /// <summary>
    /// Password for the SMB credentials.
    /// </summary>
    /// <example>Password123</example>
    [PasswordPropertyText]
    public string Password { get; set; }

    /// <summary>
    /// Defines the operating system of the SMB server.
    /// Options used to determine correct path separator to use.
    /// </summary>
    /// <example>Linux</example>
    [DefaultValue(Os.Linux)]
    public Os OperatingSystem { get; set; } = Os.Linux;

    /// <summary>
    /// Authentication mechanism to use when connecting to the SMB server.
    /// Kerberos requires network access to a KDC and a registered SPN (cifs/servername)
    /// for the target server in Active Directory.
    /// </summary>
    /// <example>Ntlm</example>
    [DefaultValue(AuthenticationMode.Ntlm)]
    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Ntlm;

    /// <summary>
    /// Hostname used to build the Kerberos SPN (cifs/hostname) when AuthenticationMode is Kerberos.
    /// Falls back to Server when not set. Only needed when the address used for the TCP
    /// connection differs from the server's registered Kerberos identity - e.g.
    /// where you connect via a mapped IP/port but the AD-registered name is something else.
    /// </summary>
    /// <example>DC1.test.local</example>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(AuthenticationMode), "", AuthenticationMode.Kerberos)]
    public string KerberosServerName { get; set; }

    /// <summary>
    /// Optional explicit KDC address (host or host:port) for Kerberos authentication.
    /// Use when DNS SRV discovery is unavailable.
    /// If empty, KDC will be discovered via DNS SRV records for the realm.
    /// </summary>
    /// <example>kdc.company.com:88</example>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(AuthenticationMode), "", AuthenticationMode.Kerberos)]
    public string KdcAddress { get; set; }
}
