using System;

namespace Frends.Smb.DeleteFiles.Definitions;

/// <summary>
/// Represents a single file that failed to move when ContinueOnFailure is enabled.
/// </summary>
public class FileFailure
{
    /// <summary>
    /// The source path of the file that failed to delete.
    /// </summary>
    /// <example>source/reports/file.txt</example>
    public string SourcePath { get; set; }

    /// <summary>
    /// The reason the file could not be deleted.
    /// </summary>
    /// <example>File 'target/reports/file.txt' already exists. No files deleted.</example>
    public string Reason { get; set; }

    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    /// <example>System.IO.IOException: Failed to open file 'reports/file.txt' for deletion: STATUS_ACCESS_DENIED</example>
    public Exception AdditionalInfo { get; set; }
}
