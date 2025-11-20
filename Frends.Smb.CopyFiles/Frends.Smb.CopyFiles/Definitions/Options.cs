using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// If true, creates the target directory and any necessary parent directories if they don't exist.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool CreateTargetDirectories { get; set; } = true;

    /// <summary>
    /// Specifies the action to take when a file with the same name already exists in the target location.
    /// </summary>
    /// <example>Throw</example>
    [DefaultValue(FileExistsAction.Throw)]
    public FileExistsAction IfTargetFileExists { get; set; } = FileExistsAction.Throw;

    /// <summary>
    /// Search for files recursively in subdirectories. Default is true.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool Recursive { get; set; } = true;

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
    /// Whether to throw an error on failure.
    /// </summary>
    /// <example>false</example>
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
