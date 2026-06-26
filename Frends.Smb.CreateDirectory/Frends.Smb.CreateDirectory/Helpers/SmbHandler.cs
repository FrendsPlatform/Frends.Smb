using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using Frends.Smb.CreateDirectory.Definitions;
using SMBLibrary;
using SMBLibrary.Client;
using static SMBLibrary.AccessMask;

namespace Frends.Smb.CreateDirectory.Helpers;

internal static class SmbHandler
{
    internal static void ValidateParameters(PathString directoryPath, Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Destination Path cannot be empty.", nameof(directoryPath));
        if (directoryPath.Value.StartsWith($"{PathString.GetSeparatorChar()}{PathString.GetSeparatorChar()}"))
            throw new ArgumentException("Path should be relative to the share, not a full UNC path.");
        if (string.IsNullOrWhiteSpace(connection.Username))
            throw new ArgumentException("Username cannot be empty.", nameof(connection));
    }

    internal static void PrepareSmbConnection(
        out SMB2Client client,
        out ISMBFileStore fileStore,
        Connection connection)
    {
        client = new SMB2Client();
        var (domain, username) = GetDomainAndUsername(connection.Username);

        bool connected = client.Connect(connection.Server, SMBTransportType.DirectTCPTransport);
        if (!connected)
            throw new Exception($"Failed to connect to SMB server: {connection.Server}");

        var status = client.Login(domain, username, connection.Password);
        if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"SMB login failed: {status}");
        fileStore = client.TreeConnect(connection.Share, out status);
        if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to connect to share: {status}");
    }

    internal static void CreateDirectory(
        ISMBFileStore fileStore,
        [NotNull] PathString smbFullPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(smbFullPath)) return;

        var parts = smbFullPath.Value.Split([PathString.GetSeparatorChar()], StringSplitOptions.RemoveEmptyEntries);

        PathString current = string.Empty;

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = string.IsNullOrEmpty(current) ? part : $"{current}{PathString.GetSeparatorChar()}{part}";

            var status = fileStore.CreateFile(
                out _,
                out _,
                current,
                SYNCHRONIZE | GENERIC_WRITE,
                FileAttributes.Directory,
                ShareAccess.Write,
                CreateDisposition.FILE_OPEN_IF,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);
            if (status != NTStatus.STATUS_SUCCESS &&
                status != NTStatus.STATUS_OBJECT_NAME_COLLISION &&
                status != NTStatus.STATUS_OBJECT_NAME_EXISTS)
            {
                throw new Exception($"Failed to create SMB directory '{current}'. NTStatus={status}");
            }
        }
    }

    private static (string domain, string user) GetDomainAndUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (string.Empty, string.Empty);
        var parts = username.Split('\\');
        if (parts.Length == 2)
            return (parts[0], parts[1]);
        return (string.Empty, username);
    }
}
