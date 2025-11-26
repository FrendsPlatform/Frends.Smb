using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.DeleteDirectory.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Full path  to the directory (relative to the share) we want to delete. If the folder already doesn't exist, nothing happens.
    /// </summary>
    /// <example>root\folder\newFolder</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string DirectoryPath { get; set; }
}
