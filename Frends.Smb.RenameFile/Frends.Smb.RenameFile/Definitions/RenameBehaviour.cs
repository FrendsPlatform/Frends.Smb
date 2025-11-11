namespace Frends.Smb.RenameFile.Definitions
{
    /// <summary>
    /// How the file rename should work if a file with the new name already exists. If Rename is selected, will append a number to the new file name, e.g. renamed(2).txt
    /// </summary>
    public enum RenameBehaviour
    {
        /// <summary>
        /// Throw an error and roll back all transfers.
        /// </summary>
        Rename,

        /// <summary>
        /// Overwrite the target file.
        /// </summary>
        Overwrite,

        /// <summary>
        /// Rename the transferred file by appending a number to the end.
        /// </summary>
        Throw,
    }
}
