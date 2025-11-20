namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Pattern used to filter files.
/// </summary>
public enum PatternMatchingMode
{
    /// <summary>
    /// Wildcards * and ? are supported.
    /// </summary>
    Wildcards = 1,

    /// <summary>
    /// Standard .NET regular expressions are supported.
    /// </summary>
    Regex = 2,
}
