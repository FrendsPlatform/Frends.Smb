using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.MoveDirectory.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// Specifies the action to take when a directory with the same name already exists in the target location.
    /// </summary>
    /// <example>Throw</example>
    [DefaultValue(DirectoryExistsAction.Throw)]
    public DirectoryExistsAction IfTargetDirectoryExists { get; set; } = DirectoryExistsAction.Throw;

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
