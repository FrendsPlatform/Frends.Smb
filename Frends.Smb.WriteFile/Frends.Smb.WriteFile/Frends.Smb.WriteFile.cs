using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Frends.Smb.WriteFile.Definitions;
using Frends.Smb.WriteFile.Helpers;
using SMBLibrary;
using SMBLibrary.Client;
using static SMBLibrary.AccessMask;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Frends.Smb.WriteFile;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Write file to SMB server.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-WriteFile)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string Path, double SizeInMegaBytes, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result WriteFile(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        var client = new SMB2Client();
        ISMBFileStore fileStore = null;

        try
        {
            if (string.IsNullOrWhiteSpace(connection.Server))
                throw new ArgumentException("Server cannot be empty.", nameof(connection));
            if (string.IsNullOrWhiteSpace(connection.Share))
                throw new ArgumentException("Share cannot be empty.", nameof(connection));
            if (string.IsNullOrWhiteSpace(input.DestinationPath))
                throw new ArgumentException("Destination Path cannot be empty.", nameof(input));
            if (input.DestinationPath.StartsWith(@"\\"))
                throw new ArgumentException("Path should be relative to the share, not a full UNC path.");

            var (domain, username) = GetDomainAndUsername(connection.Username);

            if (!IPAddress.TryParse(connection.Server, out var serverAddress))
            {
                var hostEntry = Dns.GetHostEntry(connection.Server);
                serverAddress = hostEntry.AddressList.FirstOrDefault()
                                ?? throw new Exception($"Could not resolve hostname: {connection.Server}");
            }

            var isConnected = client.Connect(serverAddress, SMBTransportType.DirectTCPTransport);
            if (!isConnected) throw new Exception("Failed to connect to SMB server");
            NTStatus status = client.Login(domain, username, connection.Password);
            if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"SMB login failed: {status}");
            fileStore = client.TreeConnect(connection.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to connect to share: {status}");

            var localMemoryStream = new MemoryStream(input.Content);
            long writeOffset = 0;
            var disposition = options.Overwrite ? CreateDisposition.FILE_OVERWRITE_IF : CreateDisposition.FILE_CREATE;

            EnsureDirectoriesExist(fileStore, input.DestinationPath);
            status = fileStore.CreateFile(
                out var fileHandle,
                out var fileStatus,
                input.DestinationPath,
                SYNCHRONIZE | GENERIC_WRITE,
                FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                disposition,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new Exception(
                    $"Failed while creating a file\nClient status: {status}\n Created file status: {fileStatus}");
            }

            while (localMemoryStream.Position < localMemoryStream.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] buffer = new byte[client.MaxWriteSize];
                var bytesRead = localMemoryStream.Read(buffer, 0, buffer.Length);
                if (bytesRead < client.MaxWriteSize)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                status = fileStore.WriteFile(out _, fileHandle, writeOffset, buffer);
                if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to write to file: {status}");
                writeOffset += bytesRead;
            }

            status = fileStore.CloseFile(fileHandle);
            if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed while closing a file: {status}");

            return new Result
            {
                Success = true,
                Path = $@"\\{connection.Server}\{connection.Share}\{input.DestinationPath.Replace('/', '\\')}",
                SizeInMegaBytes = Math.Ceiling(writeOffset / 1024.0 / 1024.0),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ErrorHandler.Handle(e, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
        finally
        {
            fileStore?.Disconnect();

            NTStatus status = NTStatus.STATUS_NOT_IMPLEMENTED;
            try
            {
                client.ListShares(out status);
            }
            catch
            {
                // ignored
            }

            if (status == NTStatus.STATUS_SUCCESS) client.Logoff();
            client.Disconnect();
        }
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        return domainAndUserName.Length != 2
            ? throw new ArgumentException($@"UserName field must be of format domain\username was: {username}")
            : new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }

    private static void EnsureDirectoriesExist(ISMBFileStore fileStore, string smbFullPath)
    {
        ArgumentNullException.ThrowIfNull(fileStore);
        if (string.IsNullOrWhiteSpace(smbFullPath)) return;

        var directory = Path.GetDirectoryName(smbFullPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(directory)) return;

        var parts = directory.Split(["/"], StringSplitOptions.RemoveEmptyEntries);

        string current = string.Empty;

        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? part : $"{current}/{part}";

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
}
