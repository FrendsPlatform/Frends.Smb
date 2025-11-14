using System;

namespace Frends.Smb.ListFiles.Definitions;

/// <summary>
/// Metadata for a file.
/// </summary>
public class FileItem
{
    /// <summary>
    /// Name of the file.
    /// </summary>
    /// <example>document.txt</example>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path of the file.
    /// </summary>
    /// <example>Folder/SubFolder/document.txt</example>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Size of the file in megabytes.
    /// </summary>
    /// <example>2</example>
    public double SizeInMegabytes { get; set; }

    /// <summary>
    /// Creation time of the file.
    /// </summary>
    /// <example>2023-02-06T11:59:13.8696745+02:00</example>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Last modification time of the file.
    /// </summary>
    /// <example>2023-02-06T11:59:13.8696745+02:00</example>
    public DateTime ModificationTime { get; set; }
}
