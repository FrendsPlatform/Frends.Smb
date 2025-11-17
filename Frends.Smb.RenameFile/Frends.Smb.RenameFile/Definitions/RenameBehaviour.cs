namespace Frends.Smb.RenameFile.Definitions
{
    /// <summary>
    /// How the file rename should work if a file with the new name already exists. If Rename is selected, will append a number to the new file name, e.g. renamed(2).txt
    /// </summary>
    public enum RenameBehaviour
    {
        /// <summary>
        /// Rename the transferred file by appending a number to avoid conflicts.
        /// </summary>
        Rename,

        /// <summary>
        /// Overwrite the target file.
        /// </summary>
        Overwrite,

        /// <summary>
        /// Throw an error if the target file already exists.
        /// </summary>
        Throw,
    }
}
