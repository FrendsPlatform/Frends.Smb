using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Source path relative to the share where files will be copied from.
    /// Can be a directory path or a specific file path.
    /// Empty means root directory.
    /// </summary>
    /// <example>documents/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Target path relative to the share where files will be copied to.
    /// Must be a directory path.
    /// Empty means root directory.
    /// </summary>
    /// <example>backup/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string TargetPath { get; set; } = string.Empty;
}
