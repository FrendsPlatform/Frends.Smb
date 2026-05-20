using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.DeleteFiles.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// Define how pattern matching will work.
    /// </summary>
    /// <example>Regex</example>
    [DefaultValue(PatternMatchingMode.Regex)]
    public PatternMatchingMode PatternMatchingMode { get; set; } = PatternMatchingMode.Regex;

    /// <summary>
    /// Regex pattern to filter files. Empty pattern matches all files.
    /// </summary>
    /// <example>*.txt</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// When enabled, the task continues deleting remaining files even if individual file operations fail.
    /// Successfully deleted files are returned in Files with Success = true. Failed files
    /// are reported in Error.FileFailures with the reason for each failure.
    /// Even if some files fail, the task will not throw regardless of the ThrowErrorOnFailure setting.
    /// </summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>
    /// Whether to throw an error on failure.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool ThrowErrorOnFailure { get; set; } = true;

    /// <summary>
    /// Overrides the error message on failure.
    /// </summary>
    /// <example>Custom error message</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string ErrorMessageOnFailure { get; set; } = string.Empty;
}
