using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.CreateDirectory.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Full path  to the directory (relative to the share) we want to create. If a folder already exists, nothing happens.
    /// </summary>
    /// <example>root\folder\newFolder</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string DirectoryPath { get; set; }
}
