using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.DeleteFiles.Definitions;

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
    /// <example>testshare</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Share { get; set; }

    /// <summary>
    /// Username for SMB authentication.
    /// This needs to be of format domain\username
    /// </summary>
    /// <example>WORKGROUP\Administrator</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string UserName { get; set; }

    /// <summary>
    /// Password for the SMB credentials.
    /// </summary>
    /// <example>Password123</example>
    [PasswordPropertyText]
    public string Password { get; set; }
}
