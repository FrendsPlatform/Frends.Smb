using System.Collections.Generic;

namespace Frends.Smb.DeleteFiles.Definitions;

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
    /// List of deleted files.
    /// </summary>
    /// <example>[ { "Name": "file1.txt", "Path": "Folder/file1.txt" } ]</example>
    public List<FileItem> FilesDeleted { get; set; }

    /// <summary>
    /// Total count of successfully deleted files.
    /// </summary>
    /// <example>5</example>
    public int TotalFilesDeleted { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}