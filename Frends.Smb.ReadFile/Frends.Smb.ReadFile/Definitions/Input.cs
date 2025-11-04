using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.ReadFile.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Full path to the file to be read.
    /// </summary>
    /// <example>folder\file.txt</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string Path { get; set; }
}