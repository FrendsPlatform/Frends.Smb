namespace Frends.Smb.MoveDirectory.Definitions;

/// <summary>
/// Defines the action to take when a target directory already exists during a move operation.
/// </summary>
public enum DirectoryExistsAction
{
    /// <summary>
    /// Throw an exception if the target directory already exists.
    /// </summary>
    Throw,

    /// <summary>
    /// Overwrite the existing target directory with the source directory.
    /// </summary>
    Overwrite,

    /// <summary>
    /// Rename the source directory to a unique name if the target directory exists.
    /// </summary>
    Rename,
}
