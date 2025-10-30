namespace Frends.Smb.ReadFile.Definitions
{
    /// <summary>
    /// SMB protocol version to use for connection.
    /// </summary>
    public enum SmbVersion
    {
        /// <summary>
        /// SMB 2.0/2.1 protocol version.
        /// Does not support encryption.
        /// </summary>
        SMB2,

        /// <summary>
        /// SMB 3.0+ protocol version.
        /// Supports encryption and enhanced security features.
        /// </summary>
        SMB3,
    }

    /// <summary>
    /// File encoding for read/write operations.
    /// </summary>
    public enum FileEncoding
    {
        /// <summary>
        /// UTF-8 encoding (Unicode, 8-bit).
        /// </summary>
        UTF8,

        /// <summary>
        /// System default encoding.
        /// </summary>
        Default,

        /// <summary>
        /// ASCII encoding (7-bit).
        /// </summary>
        ASCII,

        /// <summary>
        /// Unicode encoding (UTF-16).
        /// </summary>
        Unicode,

        /// <summary>
        /// Windows-1252 encoding (Western European).
        /// </summary>
        Windows1252,

        /// <summary>
        /// Custom encoding specified in EncodingInString property.
        /// </summary>
        Other,
    }
}
