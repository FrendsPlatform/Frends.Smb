using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using Frends.Smb.CopyFiles.Definitions;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.CopyFiles.Helpers;

internal static class SmbHandler
{
    internal static void ValidateParameters(Input input, Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));
        if (input.SourcePath.StartsWith(@"\\"))
            throw new ArgumentException("SourcePath should be relative to the share, not a full UNC path.");
        if (input.TargetPath.StartsWith(@"\\"))
            throw new ArgumentException("TargetPath should be relative to the share, not a full UNC path.");
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

        if (!IPAddress.TryParse(connection.Server, out var serverAddress))
        {
            var hostEntry = Dns.GetHostEntry(connection.Server!);
            serverAddress = hostEntry.AddressList.FirstOrDefault()
                            ?? throw new Exception($"Could not resolve hostname: {connection.Server}");
        }

        var isConnected = client.Connect(serverAddress, SMBTransportType.DirectTCPTransport);
        if (!isConnected) throw new Exception("Failed to connect to SMB server");
        var status = client.Login(domain, username, connection.Password);
        if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"SMB login failed: {status}");
        fileStore = client.TreeConnect(connection.Share, out status);
        if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to connect to share: {status}");
    }

    internal static void CreateDirectory(
        ISMBFileStore fileStore,
        [NotNull] string smbFullPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(smbFullPath)) return;

        var parts = smbFullPath.Replace('\\', '/').Split(["/"], StringSplitOptions.RemoveEmptyEntries);

        string current = string.Empty;

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = string.IsNullOrEmpty(current) ? part : $"{current}/{part}";

            var status = fileStore.CreateFile(
                out _,
                out _,
                current,
                AccessMask.SYNCHRONIZE | AccessMask.GENERIC_WRITE,
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

    internal static List<FileItem> CopyFiles(
        ISMBFileStore fileStore,
        Input input,
        Options options,
        CancellationToken cancellationToken)
    {
        return [];
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        return domainAndUserName.Length != 2
            ? throw new ArgumentException($@"Username field must be of format domain\username was: {username}")
            : new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }
}
