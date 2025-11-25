using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using Frends.Smb.DeleteDirectory.Definitions;
using SMBLibrary;
using SMBLibrary.Client;
using static SMBLibrary.AccessMask;

namespace Frends.Smb.DeleteDirectory.Helpers;

internal static class SmbHandler
{
    internal static void ValidateParameters(Input input, Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(input.DirectoryPath))
            throw new ArgumentException("Destination Path cannot be empty.", nameof(input));
        if (input.DirectoryPath.StartsWith(@"\\"))
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

    internal static void DeleteDirectory(
        ISMBFileStore fileStore,
        [NotNull] string smbFullPath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(smbFullPath)) return;

        smbFullPath = smbFullPath.Replace('\\', '/').Trim('/');

        if (!DirectoryExists(fileStore, smbFullPath))
            return;

        var entries = ListDirectoryEntries(fileStore, smbFullPath, cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var e = (FileDirectoryInformation)entry;
            if (e.FileName is "." or "..") continue;

            var fullPath = $"{smbFullPath}/{e.FileName}";

            if ((e.FileAttributes & FileAttributes.Directory) == 0)
            {
                if (!recursive)
                    throw new Exception($"Directory not empty (file found): {fullPath}");

                DeleteFileOrEmptyDirectory(fileStore, fullPath, FileAttributes.Normal);
                continue;
            }

            if (!recursive)
            {
                if (!IsDirectoryEmptyDeep(fileStore, fullPath, cancellationToken))
                    throw new Exception($"Directory not empty (non-empty subdirectory found): {fullPath}");

                DeleteFileOrEmptyDirectory(fileStore, fullPath, FileAttributes.Directory);
                continue;
            }

            DeleteDirectory(fileStore, fullPath, true, cancellationToken);
        }

        DeleteFileOrEmptyDirectory(fileStore, smbFullPath, FileAttributes.Directory);
    }

    private static bool DirectoryExists(ISMBFileStore fileStore, string path)
    {
        var status = fileStore.CreateFile(
            out var handle,
            out _,
            path,
            GENERIC_READ,
            FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS) return false;
        fileStore.CloseFile(handle);
        return true;
    }

    private static List<FileInformation> ListDirectoryEntries(
        ISMBFileStore fileStore,
        string directory,
        CancellationToken cancellationToken)
    {
        var status = fileStore.CreateFile(
            out var handle,
            out _,
            directory,
            GENERIC_READ,
            FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Cannot open directory '{directory}'. NTStatus={status}");

        var results = new List<FileInformation>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = fileStore.QueryDirectory(
                out var infoList,
                handle,
                "*",
                FileInformationClass.FileDirectoryInformation);

            if (status != NTStatus.STATUS_NO_MORE_FILES)
                throw new Exception($"QueryDirectory failed on '{directory}'. NTStatus={status}");

            results.AddRange(infoList);
        }
        finally
        {
            fileStore.CloseFile(handle);
        }

        return results;
    }

    private static void DeleteFileOrEmptyDirectory(
        ISMBFileStore fileStore,
        string path,
        FileAttributes fileAttribute)
    {
        var status = fileStore.CreateFile(
            out var handle,
            out _,
            path,
            DELETE,
            fileAttribute,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            0,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open file for delete: {path} NTStatus={status}");

        try
        {
            FileDispositionInformation fileDispositionInformation = new FileDispositionInformation
            {
                DeletePending = true,
            };
            status = fileStore.SetFileInformation(handle, fileDispositionInformation);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to delete file: {path} NTStatus={status}");
            status = fileStore.CloseFile(handle);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to close file after delete: {path} NTStatus={status}");
        }
        finally
        {
            fileStore.CloseFile(handle);
        }
    }

    private static bool IsDirectoryEmptyDeep(
        ISMBFileStore fileStore,
        string directory,
        CancellationToken cancellationToken)
    {
        var entries = ListDirectoryEntries(fileStore, directory, cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var e = (FileDirectoryInformation)entry;

            if ((e.FileAttributes & FileAttributes.Directory) == 0)
                return false;

            var subDir = $"{directory}/{e.FileName}";
            if (!IsDirectoryEmptyDeep(fileStore, subDir, cancellationToken))
                return false;
        }

        return true;
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        return domainAndUserName.Length != 2
            ? throw new ArgumentException($@"Username field must be of format domain\username was: {username}")
            : new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }
}
