using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.ReadFile.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// Encoding for the file content.
    /// By selecting 'Other' you can use any encoding.
    /// </summary>
    /// <example>FileEncoding.UTF8</example>
    [DefaultValue(FileEncoding.Utf8)]
    public FileEncoding FileEncoding { get; set; }

    /// <summary>
    /// Enable BOM (Byte Order Mark) for UTF-8 encoding.
    /// </summary>
    /// <example>false</example>
    [UIHint(nameof(FileEncoding), "", FileEncoding.Utf8)]
    [DefaultValue(false)]
    public bool EnableBom { get; set; }

    /// <summary>
    /// File encoding to be used when FileEncoding is set to 'Other'.
    /// A partial list of possible encodings: https://en.wikipedia.org/wiki/Windows_code_page#List
    /// </summary>
    /// <example>ISO-8859-2</example>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(FileEncoding), "", FileEncoding.Other)]
    public string EncodingInString { get; set; }

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
