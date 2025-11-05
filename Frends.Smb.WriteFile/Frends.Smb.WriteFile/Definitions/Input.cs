using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Smb.WriteFile.Definitions;

/// <summary>
/// Essential input parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// Content of the file to be written.
    /// </summary>
    /// <example>C:\\test.txt</example>
    [DisplayFormat(DataFormatString = "Text")]
    public byte[] Content { get; set; }

    /// <summary>
    /// Full path to the destination file we want to write to.
    /// </summary>
    /// <example>folder\file.txt</example>
    [DisplayFormat(DataFormatString = "Text")]
    public string DestinationPath { get; set; }
}
