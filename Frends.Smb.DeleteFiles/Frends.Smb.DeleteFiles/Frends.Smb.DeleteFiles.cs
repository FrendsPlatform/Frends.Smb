using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.DeleteFiles.Definitions;
using Frends.Smb.DeleteFiles.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.DeleteFiles;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Deletes files from an SMB share by path or matching pattern.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-DeleteFiles)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, List&lt;FileItem&gt; FilesDeleted, int TotalFilesDeleted, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static async Task<Result> DeleteFiles(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteDeleteAsync(input, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static async Task<Result> ExecuteDeleteAsync(
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

        input.Path ??= string.Empty;

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

                var filesToDelete =
                    await FindMatchingFilesAsync(fileStore, input.Path, options, cancellationToken);

                if (filesToDelete.Count == 0)
                {
                    throw new Exception($"No files found matching path '{input.Path}'" +
                                        (string.IsNullOrWhiteSpace(options.Pattern)
                                            ? string.Empty
                                            : $" with pattern '{options.Pattern}'"));
                }

                var deletedFiles = new List<FileItem>();
                foreach (var filePath in filesToDelete)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
                    {
                        throw new Exception($"Failed to open file '{filePath}' for deletion: {openStatus}");
                    }

                    try
                    {
                        var deleteInfo = new FileDispositionInformation { DeletePending = true };
                        NTStatus deleteStatus = fileStore.SetFileInformation(fileHandle, deleteInfo);

                        if (deleteStatus == NTStatus.STATUS_SUCCESS)
                        {
                            var normalizedFilePath = filePath.Replace('/', '\\').TrimStart('\\');
                            var normalizedForOs = normalizedFilePath
                                .Replace('\\', Path.DirectorySeparatorChar)
                                .Replace('/', Path.DirectorySeparatorChar);

                            var fileNameOnly = Path.GetFileName(normalizedForOs);

                            deletedFiles.Add(new FileItem { Name = fileNameOnly, Path = normalizedFilePath, });
                        }
                        else
                        {
                            throw new Exception($"Failed to delete file '{filePath}': {deleteStatus}");
                        }
                    }
                    finally
                    {
                        fileStore.CloseFile(fileHandle);
                    }
                }

                return new Result
                {
                    Success = true,
                    FilesDeleted = deletedFiles,
                    TotalFilesDeleted = deletedFiles.Count,
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

    private static Regex PrepareRegex(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Pattern)) return null;
        var pattern = options.Pattern;
        if (options.PatternMatchingMode == PatternMatchingMode.Wildcards)
            pattern = "^" + Regex.Escape(options.Pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static async Task<List<string>> FindMatchingFilesAsync(
        ISMBFileStore fileStore,
        string basePath,
        Options options,
        CancellationToken cancellationToken)
    {
        var matchedFiles = new List<string>();

        basePath = basePath?.Trim('\\', '/') ?? string.Empty;

        if (!string.IsNullOrEmpty(basePath))
        {
            NTStatus fileCheckStatus = fileStore.CreateFile(
                out object fileHandle,
                out FileStatus fileStatus,
                basePath,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (fileCheckStatus == NTStatus.STATUS_SUCCESS)
            {
                fileStore.CloseFile(fileHandle);
                matchedFiles.Add(basePath);
                return matchedFiles;
            }
        }

        var regex = PrepareRegex(options);

        matchedFiles = await Task.Run(
            () =>
            {
                return EnumerateFiles(fileStore, basePath, regex, cancellationToken);
            }, cancellationToken);

        return matchedFiles;
    }

    private static List<string> EnumerateFiles(ISMBFileStore fileStore, string directoryPath, Regex regex,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

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

        var files = new List<string>();

        try
        {
            NTStatus status;
            List<QueryDirectoryFileInformation> entries;

            do
            {
                token.ThrowIfCancellationRequested();

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
                    token.ThrowIfCancellationRequested();

                    string fileName = entry.FileName;
                    if (fileName == "." || fileName == "..")
                        continue;

                    bool isDir = (entry.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;
                    if (!isDir && regex.IsMatch(fileName))
                    {
                        string fullPath = string.IsNullOrEmpty(directoryPath)
                            ? fileName
                            : $"{directoryPath}\\{fileName}";

                        files.Add(fullPath);
                    }
                }
            } while (status == NTStatus.STATUS_SUCCESS);
        }
        finally
        {
            fileStore.CloseFile(dirHandle);
        }

        return files;
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        if (domainAndUserName.Length != 2)
            throw new ArgumentException($@"UserName field must be of format domain\username was: {username}");
        return new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }
}
