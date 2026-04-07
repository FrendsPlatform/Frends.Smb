using System;
using System.IO;

namespace Frends.Smb.CopyFiles.Definitions;

internal class SourceFileInfo
{
    internal SourceFileInfo(string filePath, string initialPath)
    {
        FilePath = filePath.Trim('\\');
        InitialPath = initialPath.TrimStart('\\');
        RelativeFilePath = GetRelativeFilePath();
    }

    /// <summary>
    /// File path from the root of a share
    /// </summary>
    internal string FilePath { get; }

    /// <summary>
    /// path to the file starting from InitialPath instead of share root
    /// </summary>
    internal string RelativeFilePath { get; }

    /// <summary>
    /// Initial source path that was provided as an Input to the task
    /// </summary>
    private string InitialPath { get; }

    private string GetRelativeFilePath()
    {
        // Input.SourcePath is a file
        if (InitialPath == FilePath)
        {
            return Path.GetFileName(FilePath);
        }

        // Input.SourcePath == "" (root)
        if (string.IsNullOrEmpty(InitialPath)) return FilePath;

        // General case
        if (!FilePath.StartsWith(InitialPath))
            throw new Exception($"File '{FilePath}' is not under source path '{InitialPath}'");
        var lastDir = Path.GetFileName(InitialPath);
        string leftover = FilePath[InitialPath.Length..];
        return $"{lastDir}\\{leftover.Trim('\\')}";
    }
}
