using System;

namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Represents a single file that failed to move when ContinueOnFailure is enabled.
/// </summary>
public class FileFailure
{
    /// <summary>
    /// The source path of the file that failed to copy.
    /// </summary>
    /// <example>source/reports/file.txt</example>
    public string SourcePath { get; set; }

    /// <summary>
    /// The target path of the file that failed to copy.
    /// </summary>
    /// <example>target/reports/file.txt</example>
    public string TargetPath { get; set; }

    /// <summary>
    /// The reason the file could not be copied.
    /// </summary>
    /// <example>File 'target/reports/file.txt' already exists. No files copied.</example>
    public string Reason { get; set; }

    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    /// <example>System.IO.IOException: File 'target/reports/file.txt' already exists.</example>
    public Exception AdditionalInfo { get; set; }
}
