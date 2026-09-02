using System;

namespace Frends.Smb.CopyFiles.Definitions;

/// <summary>
/// Wraps a string path value and normalizes separators on assignment.
/// By default, Slash is set up as PathSeparator
/// </summary>
public class PathString : IEquatable<string>, IEquatable<PathString>
{
    private string value = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathString"/> class.
    /// </summary>
    /// <param name="val"> value to set</param>
    public PathString(string val)
    {
        Value = val;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PathString"/> class with default value.
    /// </summary>
    public PathString()
    {
        Value = string.Empty;
    }

    /// <summary>
    /// Gets or sets the normalized path value.
    /// </summary>
    /// <value>The normalized path string.</value>
    /// <example>folder\file.txt</example>
    public string Value
    {
        get => value;
        set => this.value = Normalize(value);
    }

    private static Separator PathSeparator { get; set; } = Separator.Slash;

    /// <summary>
    /// Converts a string to a normalized path string wrapper.
    /// </summary>
    /// <param name="value">Path value to normalize.</param>
    /// <returns>Normalized wrapper instance.</returns>
    public static implicit operator PathString(string value) => new()
    {
        Value = value,
    };

    /// <summary>
    /// Converts a path string wrapper to a string.
    /// </summary>
    /// <param name="path">Path string wrapper.</param>
    /// <returns>Normalized string value.</returns>
    public static implicit operator string(PathString path) => path?.Value;

    /// <summary>
    /// Compares two PathString instances for value equality.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when both are equal or both are null.</returns>
    public static bool operator ==(PathString left, PathString right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Value == right.Value;
    }

    /// <summary>
    /// Compares two PathString instances for value inequality.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when values differ.</returns>
    public static bool operator !=(PathString left, PathString right) => !(left == right);

    /// <summary>
    /// Configures the global separator used by path strings.
    /// </summary>
    /// <param name="os">OperatingSystem that will define used separator</param>
    public static void Setup(Os os)
    {
        PathSeparator = os switch
        {
            Os.Windows => Separator.Backslash,
            Os.Linux => Separator.Slash,
            _ => throw new ArgumentException($"Unsupported operating system: {os}"),
        };
    }

    /// <summary>
    /// Returns the normalized string value.
    /// </summary>
    /// <returns>Normalized path string.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Determines whether the path value equals the given string.
    /// </summary>
    /// <param name="other">The string to compare with.</param>
    /// <returns>True if equal; otherwise false.</returns>
    public bool Equals(string other) => Value == other;

    /// <summary>
    /// Determines whether this instance equals another PathString.
    /// </summary>
    /// <param name="other">The PathString to compare with.</param>
    /// <returns>True if equal; otherwise false.</returns>
    public bool Equals(PathString other) => other is not null && Value == other.Value;

    /// <summary>
    /// Determines whether this instance equals another object.
    /// Supports direct comparison with string and other PathString instances.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if equal; otherwise false.</returns>
    public override bool Equals(object obj)
    {
        return obj switch
        {
            string s => Value == s,
            PathString p => Value == p.Value,
            _ => obj?.ToString() == Value,
        };
    }

    /// <summary>
    /// Returns the hash code for the normalized path value.
    /// </summary>
    /// <returns>hash code</returns>
    public override int GetHashCode() => Value.GetHashCode();

    internal static char GetSeparatorChar() => PathSeparator == Separator.Slash ? '/' : '\\';

    /// <summary>
    /// Returns the file name portion of the given path using the separator configured
    /// via <see cref="Setup"/>, instead of relying on System.IO.Path (whose separator
    /// recognition depends on the OS the process is running on, not on the configured
    /// SMB server OS).
    /// </summary>
    /// <param name="path">Path string wrapper.</param>
    /// <returns>The file name portion of the path.</returns>
    internal static string GetFileName(PathString path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int index = path.Value.LastIndexOf(GetSeparatorChar());
        return index < 0 ? path.Value : path.Value[(index + 1)
            ..];
    }

    /// <summary>
    /// Returns the directory portion of the given path using the configured separator.
    /// </summary>
    /// <param name="path">Path string wrapper.</param>
    /// <returns>The directory portion of the path.</returns>
    internal static string GetDirectoryName(PathString path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int index = path.Value.LastIndexOf(GetSeparatorChar());
        return index < 0 ? string.Empty : path.Value[..index];
    }

    /// <summary>
    /// Returns the file name without its extension, using the configured separator.
    /// </summary>
    /// <param name="path">Path string wrapper.</param>
    /// <returns>The file name without its extension.</returns>
    internal static string GetFileNameWithoutExtension(PathString path)
    {
        string fileName = GetFileName(path);
        int dotIndex = fileName.LastIndexOf('.');
        return dotIndex <= 0 ? fileName : fileName[..dotIndex];
    }

    /// <summary>
    /// Returns the extension (including the leading dot) of the given path.
    /// </summary>
    /// <param name="path">Path string wrapper.</param>
    /// <returns>The extension of the path, including the leading dot.</returns>
    internal static string GetExtension(PathString path)
    {
        string fileName = GetFileName(path);
        int dotIndex = fileName.LastIndexOf('.');
        return dotIndex <= 0 ? string.Empty : fileName[dotIndex..];
    }

    /// <summary>
    /// Combines two path segments using the configured separator.
    /// </summary>
    /// <param name="path1">The first path segment.</param>
    /// <param name="path2">The second path segment.</param>
    /// <returns>The combined path.</returns>
    internal static string Combine(PathString path1, PathString path2)
    {
        string left = path1?.Value ?? string.Empty;
        string right = path2?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        return $"{left.TrimEnd(GetSeparatorChar())}{GetSeparatorChar()}{right.TrimStart(GetSeparatorChar())}";
    }

    /// <summary>
    /// Normalizes path separators in the input string.
    /// </summary>
    /// <param name="input">The path string to normalize.</param>
    /// <returns>Path string with separators normalized to the configured separator.</returns>
    private static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        return input
            .Replace('\\', GetSeparatorChar())
            .Replace('/', GetSeparatorChar());
    }
}
