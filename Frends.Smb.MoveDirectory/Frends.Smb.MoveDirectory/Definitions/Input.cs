using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.MoveDirectory.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Source path relative to the share where directory will be moved from.
    /// </summary>
    /// <example>documents/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Target path relative to the share where directory will be moved to.
    /// </summary>
    /// <example>backup/reports</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string TargetPath { get; set; } = string.Empty;
}
