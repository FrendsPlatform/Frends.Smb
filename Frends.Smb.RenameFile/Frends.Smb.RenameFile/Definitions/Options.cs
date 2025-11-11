using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.RenameFile.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// How the file write should work if a file with the new name already exists
    /// </summary>
    /// <example>WriteBehaviour.Throw</example>
    [DefaultValue(RenameBehaviour.Throw)]
    public RenameBehaviour RenameBehaviour { get; set; }

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
