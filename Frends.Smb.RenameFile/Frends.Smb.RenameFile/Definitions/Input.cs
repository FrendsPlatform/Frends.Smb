using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.RenameFile.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Path to the file to be renamed.
    /// </summary>
    /// <example>folder\oldfile.txt</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The new filename including extension.
    /// </summary>
    /// <example>newfile.txt</example>
    [DefaultValue("")]
    public string NewFileName { get; set; } = string.Empty;
}
