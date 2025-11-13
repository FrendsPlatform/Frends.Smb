using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.ListFiles.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// Whether to list files recursively or not.
    /// </summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool SearchRecursively { get; set; }

    /// <summary>
    /// Define if a pattern will use wildcards.
    /// </summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool UseWildcards { get; set; }

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
