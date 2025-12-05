using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Frends.Smb.MoveDirectory.Definitions;
using Frends.Smb.MoveDirectory.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.MoveDirectory;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Smbes the input string the specified number of times.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-MoveDirectory)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string Output, object Error { string Message, Exception AdditionalInfo } }</returns>
    // TODO: Remove Connection parameter if the task does not make connections
    public static Result MoveDirectory(
     [PropertyTab] Input input,
     [PropertyTab] Connection connection,
     [PropertyTab] Options options,
     CancellationToken cancellationToken)
    {
        try
        {
            return ExecuteMoveDirectory(input, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static Result ExecuteMoveDirectory(
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

        input.SourcePath ??= string.Empty;
        input.TargetPath ??= string.Empty;

        if (string.IsNullOrWhiteSpace(input.SourcePath))
            throw new ArgumentException("SourcePath cannot be empty.", nameof(input));

        if (string.IsNullOrWhiteSpace(input.TargetPath))
            throw new ArgumentException("TargetPath cannot be empty.", nameof(input));

        if (input.SourcePath.StartsWith(@"\\"))
            throw new ArgumentException("SourcePath should be relative to the share, not a full UNC path.");

        if (input.TargetPath.StartsWith(@"\\"))
            throw new ArgumentException("TargetPath should be relative to the share, not a full UNC path.");

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

                string normalizedSourcePath = input.SourcePath.Replace('/', '\\').TrimStart('\\');
                string normalizedTargetPath = input.TargetPath.Replace('/', '\\').TrimStart('\\');

                if (!DirectoryExists(fileStore, normalizedSourcePath))
                    throw new Exception($"Source directory '{normalizedSourcePath}' does not exist.");

                string finalTargetPath = normalizedTargetPath;
                bool targetExists = DirectoryExists(fileStore, normalizedTargetPath);

                switch (options.IfTargetDirectoryExists)
                {
                    case DirectoryExistsAction.Throw:
                        if (targetExists)
                            throw new IOException($"Target directory '{normalizedTargetPath}' already exists. No directory moved.");
                        break;

                    case DirectoryExistsAction.Overwrite:
                        if (targetExists)
                        {
                            DeleteDirectoryRecursive(fileStore, normalizedTargetPath, cancellationToken);
                        }

                        break;

                    case DirectoryExistsAction.Rename:
                        if (targetExists)
                        {
                            finalTargetPath = GetNonConflictingDirectoryPath(fileStore, normalizedTargetPath);
                        }

                        break;

                    default:
                        throw new ArgumentException($"Unknown DirectoryExistsAction: {options.IfTargetDirectoryExists}");
                }

                string targetParentDir = GetDirectoryPath(finalTargetPath);
                if (!string.IsNullOrEmpty(targetParentDir) && !DirectoryExists(fileStore, targetParentDir))
                {
                    EnsureDirectoryExists(fileStore, targetParentDir);
                }

                NTStatus openStatus = fileStore.CreateFile(
                    out object dirHandle,
                    out FileStatus fileStatus,
                    normalizedSourcePath,
                    AccessMask.SYNCHRONIZE | AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE | AccessMask.DELETE,
                    SMBLibrary.FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                    null);

                if (openStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception($"Failed to open source directory '{normalizedSourcePath}' for move: {openStatus}");

                try
                {
                    var renameInfo = new FileRenameInformationType2
                    {
                        ReplaceIfExists = options.IfTargetDirectoryExists == DirectoryExistsAction.Overwrite,
                        RootDirectory = 0,
                        FileName = finalTargetPath,
                    };

                    NTStatus renameStatus = fileStore.SetFileInformation(dirHandle, renameInfo);
                    if (renameStatus != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Failed to move directory '{normalizedSourcePath}' to '{finalTargetPath}': {renameStatus}");
                }
                finally
                {
                    fileStore.CloseFile(dirHandle);
                }

                return new Result
                {
                    Success = true,
                    SourcePath = normalizedSourcePath,
                    TargetPath = finalTargetPath,
                    Error = null,
                };
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

    private static bool DirectoryExists(ISMBFileStore fileStore, string path)
    {
        NTStatus status = fileStore.CreateFile(
            out object handle,
            out FileStatus fileStatus,
            path,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status == NTStatus.STATUS_SUCCESS)
        {
            fileStore.CloseFile(handle);
            return true;
        }

        return false;
    }

    private static string GetNonConflictingDirectoryPath(ISMBFileStore fileStore, string destPath)
    {
        string candidate = destPath;
        int count = 1;
        string parentDir = GetDirectoryPath(destPath);
        string dirName = GetDirectoryName(destPath);

        while (DirectoryExists(fileStore, candidate))
        {
            string newDirName = $"{dirName}({count++})";
            candidate = string.IsNullOrEmpty(parentDir)
                ? newDirName
                : $"{parentDir}\\{newDirName}";
        }

        return candidate;
    }

    private static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        string normalized = path.Replace('/', '\\').TrimEnd('\\');
        int lastSlash = normalized.LastIndexOf('\\');
        return lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
    }

    private static void DeleteDirectoryRecursive(ISMBFileStore fileStore, string directoryPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(directoryPath) || directoryPath == "\\" || directoryPath == "/")
        {
            directoryPath = string.Empty;
        }
        else
        {
            directoryPath = directoryPath.Trim('\\', '/');
        }

        NTStatus openStatus = fileStore.CreateFile(
            out object dirHandle,
            out FileStatus fileStatus,
            directoryPath,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (openStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open directory '{directoryPath}': {openStatus}");

        try
        {
            NTStatus status;
            List<QueryDirectoryFileInformation> entries;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                status = fileStore.QueryDirectory(
                    out entries,
                    dirHandle,
                    "*",
                    FileInformationClass.FileDirectoryInformation);

                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                    throw new Exception($"QueryDirectory failed for '{directoryPath}': {status}");

                if (entries == null || entries.Count == 0)
                    continue;

                foreach (var entry in entries.OfType<FileDirectoryInformation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = entry.FileName;
                    if (fileName == "." || fileName == "..")
                        continue;

                    string fullPath = string.IsNullOrEmpty(directoryPath)
                        ? fileName
                        : $"{directoryPath}\\{fileName}";

                    bool isDir = (entry.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;

                    if (isDir)
                    {
                        DeleteDirectoryRecursive(fileStore, fullPath, cancellationToken);
                    }
                    else
                    {
                        DeleteFile(fileStore, fullPath);
                    }
                }
            }
            while (status == NTStatus.STATUS_SUCCESS);
        }
        finally
        {
            fileStore.CloseFile(dirHandle);
        }

        DeleteEmptyDirectory(fileStore, directoryPath);
    }

    private static void DeleteEmptyDirectory(ISMBFileStore fileStore, string directoryPath)
    {
        NTStatus openStatus = fileStore.CreateFile(
            out object dirHandle,
            out FileStatus fileStatus,
            directoryPath,
            AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (openStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open directory '{directoryPath}' for deletion: {openStatus}");

        try
        {
            var deleteInfo = new FileDispositionInformation { DeletePending = true };
            NTStatus deleteStatus = fileStore.SetFileInformation(dirHandle, deleteInfo);

            if (deleteStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to delete directory '{directoryPath}': {deleteStatus}");
        }
        finally
        {
            fileStore.CloseFile(dirHandle);
        }
    }

    private static string GetDirectoryPath(string filePath)
    {
        string normalized = filePath.Replace('/', '\\').TrimEnd('\\');
        int lastSlash = normalized.LastIndexOf('\\');
        return lastSlash > 0 ? normalized.Substring(0, lastSlash) : string.Empty;
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

    private static void EnsureDirectoryExists(ISMBFileStore fileStore, string directoryPath)
    {
        directoryPath = directoryPath.Trim('\\', '/');
        if (string.IsNullOrEmpty(directoryPath))
            return;

        NTStatus checkStatus = fileStore.CreateFile(
            out object checkHandle,
            out FileStatus fileStatus,
            directoryPath,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (checkStatus == NTStatus.STATUS_SUCCESS)
        {
            fileStore.CloseFile(checkHandle);
            return;
        }

        string parentDir = GetDirectoryPath(directoryPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            EnsureDirectoryExists(fileStore, parentDir);
        }

        NTStatus createStatus = fileStore.CreateFile(
            out object dirHandle,
            out fileStatus,
            directoryPath,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_CREATE,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (createStatus == NTStatus.STATUS_SUCCESS)
        {
            fileStore.CloseFile(dirHandle);
        }
        else if (createStatus != NTStatus.STATUS_OBJECT_NAME_COLLISION)
        {
            throw new Exception($"Failed to create directory '{directoryPath}': {createStatus}");
        }
    }

    private static void DeleteFile(ISMBFileStore fileStore, string filePath)
    {
        NTStatus openStatus = fileStore.CreateFile(
            out object fileHandle,
            out FileStatus fileStatus,
            filePath,
            AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (openStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open file for deletion '{filePath}': {openStatus}");

        try
        {
            var deleteInfo = new FileDispositionInformation { DeletePending = true };
            NTStatus deleteStatus = fileStore.SetFileInformation(fileHandle, deleteInfo);

            if (deleteStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to delete file '{filePath}': {deleteStatus}");
        }
        finally
        {
            fileStore.CloseFile(fileHandle);
        }
    }
}