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
    /// Kerberos authentication protocol using username and password.
    /// Kerberos.NET handles the full authentication flow internally.
    /// Requires network access to a KDC and a registered SPN (cifs/servername)
    /// for the target server in Active Directory.
    /// </summary>
    Kerberos,

    /// <summary>
    /// Kerberos authentication protocol using a pre-existing ticket cache (ccache) file.
    /// Requires a valid TGT in the ccache file created externally e.g. via kinit.
    /// </summary>
    KerberosTicketCache,
}
