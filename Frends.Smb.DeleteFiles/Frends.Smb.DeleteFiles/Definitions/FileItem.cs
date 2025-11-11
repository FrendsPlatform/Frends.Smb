namespace Frends.Smb.DeleteFiles.Definitions
{
    /// <summary>
    /// Represents a deleted file.
    /// </summary>
    public class FileItem
    {
        /// <summary>
        /// Name of the deleted file.
        /// </summary>
        /// <example>document.txt</example>
        public string Name { get; set; }

        /// <summary>
        /// Full path of the deleted file.
        /// </summary>
        /// <example>Folder/SubFolder/document.txt</example>
        public string Path { get; set; }
    }
}
