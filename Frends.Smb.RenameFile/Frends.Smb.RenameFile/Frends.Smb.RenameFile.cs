using System;
using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.RenameFile.Definitions;
using Frends.Smb.RenameFile.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.RenameFile;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Renames a file on a remote SMB share.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-RenameFile)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string NewFilePath, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static async Task<Result> RenameFile(
     [PropertyTab] Input input,
     [PropertyTab] Connection connection,
     [PropertyTab] Options options,
     CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteRenameAsync(input, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static Task<Result> ExecuteRenameAsync(
        Input input,
        Connection connection,
        Options options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));

        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));

        if (string.IsNullOrWhiteSpace(input.Path))
            throw new ArgumentException("Path cannot be empty.", nameof(input));

        if (string.IsNullOrWhiteSpace(input.NewFileName))
            throw new ArgumentException("NewFileName cannot be empty.", nameof(input));

        if (input.Path.StartsWith(@"\\"))
            throw new ArgumentException("Path should be relative to the share, not a full UNC path.");

        var (domain, user) = GetDomainAndUsername(connection.Username);

        SMB2Client client = new();
        try
        {
            bool connected = client.Connect(connection.Server, SMBTransportType.DirectTCPTransport);
            if (!connected)
                throw new Exception($"Failed to connect to SMB server: {connection.Server}");

            NTStatus loginStatus = client.Login(domain, user, connection.Password);
            if (loginStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"SMB login failed: {loginStatus}");

            ISMBFileStore fileStore = client.TreeConnect(connection.Share, out NTStatus treeStatus);
            if (treeStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to connect to share '{connection.Share}': {treeStatus}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string directory = Path.GetDirectoryName(input.Path)?.Replace('/', '\\')?.TrimStart('\\') ?? string.Empty;
                string oldFileName = Path.GetFileName(input.Path);
                string newFileName = input.NewFileName;

                string newFilePath = string.IsNullOrEmpty(directory)
                    ? newFileName
                    : $"{directory}\\{newFileName}";

                switch (options.RenameBehaviour)
                {
                    case RenameBehaviour.Throw:
                        {
                            if (FileExists(fileStore, newFilePath))
                                throw new IOException($"File already exists: {newFilePath}. No file renamed.");
                            break;
                        }

                    case RenameBehaviour.Overwrite:
                        {
                            break;
                        }

                    case RenameBehaviour.Rename:
                        {
                            newFilePath = GetNonConflictingFilePath(fileStore, newFilePath);
                            break;
                        }
                }

                NTStatus openStatus = fileStore.CreateFile(
                    out object fileHandle,
                    out FileStatus fileStatus,
                    input.Path,
                    AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
                    SMBLibrary.FileAttributes.Normal,
                    ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                    null);

                if (openStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception($"Failed to open file '{input.Path}' for rename: {openStatus}");

                try
                {
                    var renameInfo = new FileRenameInformationType2
                    {
                        ReplaceIfExists = options.RenameBehaviour == RenameBehaviour.Overwrite,
                        RootDirectory = 0,
                        FileName = newFilePath,
                    };

                    NTStatus renameStatus = fileStore.SetFileInformation(fileHandle, renameInfo);
                    if (renameStatus != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Failed to rename file '{input.Path}' to '{newFilePath}': {renameStatus}");
                }
                finally
                {
                    fileStore.CloseFile(fileHandle);
                }

                var result = new Result
                {
                    Success = true,
                    Error = null,
                    NewFilePath = newFilePath.TrimStart('\\'),
                };

                return Task.FromResult(result);
            }
            finally
            {
                fileStore.Disconnect();
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static bool FileExists(ISMBFileStore fileStore, string path)
    {
        NTStatus status = fileStore.CreateFile(
           out object handle,
           out FileStatus fileStatus,
           path,
           AccessMask.GENERIC_READ,
           SMBLibrary.FileAttributes.Normal,
           ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
           CreateDisposition.FILE_OPEN,
           CreateOptions.FILE_NON_DIRECTORY_FILE,
           null);

        if (status == NTStatus.STATUS_SUCCESS)
        {
            fileStore.CloseFile(handle);
            return true;
        }

        return false;
    }

    private static void DeleteFile(ISMBFileStore fileStore, string path)
    {
        NTStatus openStatus = fileStore.CreateFile(
            out object handle,
            out FileStatus fileStatus,
            path,
            AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        Console.WriteLine($"DeleteFile: openStatus={openStatus} for {path}");

        if (openStatus == NTStatus.STATUS_SUCCESS)
        {
            var deleteInfo = new FileDispositionInformation { DeletePending = true };
            NTStatus deleteStatus = fileStore.SetFileInformation(handle, deleteInfo);

            Console.WriteLine($"DeleteFile: deleteStatus={deleteStatus}");

            fileStore.CloseFile(handle);
        }
        else
        {
            Console.WriteLine($"DeleteFile: FAILED to open file {path} ({openStatus})");
        }
    }

    private static string GetNonConflictingFilePath(ISMBFileStore fileStore, string destPath)
    {
        string candidate = destPath;
        int count = 1;
        string dir = Path.GetDirectoryName(destPath);
        string baseName = Path.GetFileNameWithoutExtension(destPath);
        string ext = Path.GetExtension(destPath);

        while (FileExists(fileStore, candidate))
        {
            candidate = $"{dir}\\{baseName}({count++}){ext}";
        }

        return candidate;
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var parts = username.Split('\\');
        if (parts.Length != 2)
            throw new ArgumentException($@"UserName field must be of format domain\username was: {username}");
        return new Tuple<string, string>(parts[0], parts[1]);
    }
}
