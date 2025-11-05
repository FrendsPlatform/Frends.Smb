using System;

namespace Frends.Smb.WriteFile.Definitions;

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
    /// Full path to the written file.
    /// </summary>
    /// <example>\\server\share\dir\file.txt</example>
    public string Path { get; set; }

    /// <summary>
    /// Size of the written file in megabytes.
    /// </summary>
    /// <example>32</example>
    public double SizeInMegaBytes { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}
