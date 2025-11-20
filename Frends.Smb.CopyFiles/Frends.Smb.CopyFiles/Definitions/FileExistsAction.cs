namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Defines the action to take when a target file already exists during a move operation.
/// </summary>
public enum FileExistsAction
{
    /// <summary>
    /// Throw an exception if the target file already exists.
    /// </summary>
    Throw = 1,

    /// <summary>
    /// Overwrite the existing target file with the source file.
    /// </summary>
    Overwrite = 2,

    /// <summary>
    /// Rename the source file to a unique name if the target file exists (e.g., file_1.txt, file_2.txt).
    /// </summary>
    Rename = 3,
}
