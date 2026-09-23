namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Specifies authentication mode.
/// </summary>
public enum AuthenticationMode
{
    /// <summary>
    /// NTLM authentication protocol.
    /// </summary>
    Ntlm,

    /// <summary>
    /// Kerberos authentication protocol.
    /// </summary>
    Kerberos,

    /// <summary>
    /// Kerberos authentication protocol using a pre-existing ticket cache (ccache) file.
    /// </summary>
    KerberosTicketCache,
}
