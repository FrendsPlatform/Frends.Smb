using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.MoveFiles.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Source path relative to the share where files will be moved from.
    /// Can be a directory path or a specific file path.
    /// </summary>
    /// <example>documents/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Target path relative to the share where files will be moved to.
    /// Must be a directory path.
    /// </summary>
    /// <example>backup/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string TargetPath { get; set; } = string.Empty;
}
