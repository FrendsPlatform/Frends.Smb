using System;

namespace Frends.Smb.RenameFile.Definitions;

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
    /// Returns the hash code for the normalized path value.
    /// </summary>
    /// <returns>Hash code of the path value.</returns>
    private static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        return input
            .Replace('\\', GetSeparatorChar())
            .Replace('/', GetSeparatorChar());
    }
}
