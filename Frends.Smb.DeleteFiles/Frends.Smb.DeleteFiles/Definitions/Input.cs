using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.DeleteFiles.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Path to the file or directory to delete, relative to the share.
    /// If a file path is provided, only that file is deleted.
    /// If a directory path is provided, only files located directly in that directory are deleted.
    /// </summary>
    /// <example>folder/subfolder/</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// File name pattern to match files to delete within the specified share and path.
    /// Supports simple wildcards (* and ?) as well as regular expressions.
    /// </summary>
    /// <example>*.txt, report_*.csv, data?.xml, &lt;regex&gt;^log_\\d{{4}}\\.txt$</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string Pattern { get; set; } = string.Empty;
}
