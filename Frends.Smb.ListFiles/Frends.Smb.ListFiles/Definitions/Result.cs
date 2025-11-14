namespace Frends.Smb.ListFiles.Definitions;

/// <summary>
/// Result of the task.
/// </summary>
public class Result
{
    /// <summary>
    /// Indicates if the task completed successfully.
    /// </summary>
    /// <example>true</example>
    public bool Success { get; set; }

    /// <summary>
    /// List of files in a share
    /// </summary>
    /// <example>[files/temp/foo.txt, files/temp/bar.txt]</example>
    public FileItem[] Files { get; set; } = [];

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error? Error { get; set; }
}
