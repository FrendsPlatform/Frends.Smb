using System;
using System.Collections.Generic;

namespace Frends.Smb.MoveFiles.Definitions;

/// <summary>
/// Error that occurred during the task.
/// </summary>
public class Error
{
    /// <summary>
    /// Summary of the error.
    /// </summary>
    /// <example>Unable to move files.</example>
    public string Message { get; set; }

    /// <summary>
    /// Additional information about the error.
    /// </summary>
    /// <example>object { Exception AdditionalInfo }</example>
    public Exception AdditionalInfo { get; set; }

    /// <summary>
    /// Per-file failure details, populated when ContinueOnFailure is enabled.
    /// Each entry contains the source path of the file that failed and the reason.
    /// </summary>
    /// <example>
    /// [
    ///   {
    ///     "SourcePath": "source/file.txt",
    ///     "TargetPath": "target/file.txt",
    ///     "Reason": "File 'target/file.txt' already exists. No files moved.",
    ///     "AdditionalInfo": { ... }
    ///   }
    /// ]
    /// </example>
    public List<FileFailure> FileFailures { get; set; }
}
