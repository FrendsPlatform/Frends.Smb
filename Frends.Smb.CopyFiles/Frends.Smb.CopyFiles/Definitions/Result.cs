using System.Collections.Generic;

namespace Frends.Smb.CopyFiles.Definitions;

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
    /// List of files that were successfully moved, including their source and target paths.
    /// </summary>
    /// <example>[{SourcePath: "documents\report.txt", TargetPath: "archive\report.txt"}]</example>
    public List<FileItem> Files { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}
