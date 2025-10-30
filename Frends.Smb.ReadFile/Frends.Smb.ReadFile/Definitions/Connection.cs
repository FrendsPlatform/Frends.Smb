using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.ReadFile.Definitions;

/// <summary>
/// Connection parameters.
/// </summary>
public class Connection
{
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
