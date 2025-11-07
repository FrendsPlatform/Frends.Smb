using System;

namespace Frends.Smb.ReadFile.Definitions;

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
    /// File content as a byte array.
    /// This property contains the raw bytes of the file, suitable for both text and binary files.
    /// </summary>
    /// <example>byte[] { 72, 101, 108, 108, 111 }</example>
    public byte[] Content { get; set; }

    /// <summary>
    /// File content as a decoded text string (if applicable).
    /// This is populated only if the file can be successfully decoded using the specified encoding.
    /// </summary>
    /// <example>Hello World</example>
    public string TextContent { get; set; }

    /// <summary>
    /// The full path of the file that was read.
    /// </summary>
    /// <example>c:\temp\foo.txt</example>
    public string Path { get; set; }

    /// <summary>
    /// Size of the read file in megabytes.
    /// </summary>
    /// <example>32</example>
    public double SizeInMegaBytes { get; set; }

    /// <summary>
    /// DateTime when file was created.
    /// </summary>
    /// <example>2023-01-31T12:54:17.6431957+02:00</example>
    public DateTime? CreationTime { get; set; }

    /// <summary>
    /// DateTime for last write time of the file.
    /// </summary>
    /// <example>2023-02-06T11:59:13.8696745+02:00</example>
    public DateTime? LastWriteTime { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}