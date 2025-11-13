using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.CreateDirectory.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// The input string to be repeated and output.
    /// </summary>
    /// <example>foobar</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("Lorem ipsum dolor sit amet.")]
    public string Content { get; set; }

    /// <summary>
    /// Number of times to repeat the input string.
    /// </summary>
    /// <example>2</example>
    [DefaultValue(3)]
    public int Repeat { get; set; }
}
