namespace Frends.Smb.DeleteDirectory.Definitions;

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
    /// Requires a valid TGT in the ccache file created externally e.g. via kinit.
    /// </summary>
    KerberosTicketCache,
}
