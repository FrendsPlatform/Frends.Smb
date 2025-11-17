using System;

namespace Frends.Smb.ListFiles.Definitions;

/// <summary>
/// Error that occurred during the task.
/// </summary>
public class Error
{
    /// <summary>
    /// Summary of the error.
    /// </summary>
    /// <example>Unable to list files.</example>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional information about the error.
    /// </summary>
    /// <example>object { Exception AdditionalInfo }</example>
    public Exception? AdditionalInfo { get; set; }
}
