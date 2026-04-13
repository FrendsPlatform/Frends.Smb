namespace Frends.Smb.CreateDirectory.Definitions;

/// <summary>
/// Operating system type for SMB connection. Used to determine the default SMB path separator.
/// </summary>
public enum Os
{
    // self-explanatory enum values
#pragma warning disable CS1591
#pragma warning disable SA1602
    Windows = 1,
    Linux = 2,
#pragma warning restore CS1591
#pragma warning restore SA1602
}
