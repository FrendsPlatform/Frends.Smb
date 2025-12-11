namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Represents a single file that was successfully copied.
/// </summary>
public class FileItem
{
    /// <summary>
    /// The original path of the file relative to the share before it was copied.
    /// </summary>
    /// <example>documents\reports\report.txt</example>
    public string SourcePath { get; set; }

    /// <summary>
    /// The new path of the file relative to the share after it was copied.
    /// </summary>
    /// <example>backup\reports\report.txt</example>
    public string TargetPath { get; set; }
}
