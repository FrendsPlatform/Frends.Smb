using System.IO;

namespace Frends.Smb.CopyFiles.Helpers;

internal static class StringExtensions
{
    internal static string ToOsPath(this string str)
    {
        return str.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    internal static string ToSmbPath(this string str)
    {
        return str.Replace('/', '\\');
    }
}
