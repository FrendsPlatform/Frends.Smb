using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.ListFiles.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Directory from which we want to list files. If you want to list files from the root share, use "/"
    /// </summary>
    /// <example>files/temp</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Directory { get; set; } = string.Empty;
}
