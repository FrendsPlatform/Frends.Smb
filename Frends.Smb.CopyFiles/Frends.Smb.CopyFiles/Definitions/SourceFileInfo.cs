using System;
using System.IO;

namespace Frends.Smb.CopyFiles.Definitions;

internal class SourceFileInfo
{
    internal SourceFileInfo(PathString filePath, PathString initialPath)
    {
        FilePath = filePath.Value.Trim(PathString.GetSeparatorChar());
        InitialPath = initialPath.Value.TrimStart(PathString.GetSeparatorChar());
        RelativeFilePath = GetRelativeFilePath();
    }

    /// <summary>
    /// File path from the root of a share
    /// </summary>
    internal PathString FilePath { get; }

    /// <summary>
    /// path to the file starting from InitialPath instead of share root
    /// </summary>
    internal PathString RelativeFilePath { get; }

    /// <summary>
    /// Initial source path that was provided as an Input to the task
    /// </summary>
    private PathString InitialPath { get; }

    private PathString GetRelativeFilePath()
    {
        // Input.SourcePath is a file
        if (InitialPath == FilePath)
        {
            return Path.GetFileName(FilePath);
        }

        // Input.SourcePath == "" (root)
        if (string.IsNullOrEmpty(InitialPath)) return FilePath;

        // General case
        if (!FilePath.Value.StartsWith(InitialPath))
            throw new Exception($"File '{FilePath}' is not under source path '{InitialPath}'");
        PathString lastDir = Path.GetFileName(InitialPath);
        PathString leftover = FilePath.Value[InitialPath.Value.Length..];
        return $"{lastDir}{PathString.GetSeparatorChar()}{leftover.Value.Trim(PathString.GetSeparatorChar())}";
    }
}
