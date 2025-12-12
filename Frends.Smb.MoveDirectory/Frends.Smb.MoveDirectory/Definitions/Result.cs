namespace Frends.Smb.MoveDirectory.Definitions;

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
    /// The original path of the directory that was moved, relative to the share.
    /// </summary>
    /// <example>Projects\OldApp</example>
    public string SourcePath { get; set; }

    /// <summary>
    /// The new path of the directory after the move operation, relative to the share.
    /// </summary>
    /// <example>Archive\OldApp</example>
    public string TargetPath { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}