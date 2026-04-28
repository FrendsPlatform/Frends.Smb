using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Frends.Smb.MoveFiles.Definitions;
using Frends.Smb.MoveFiles.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.MoveFiles;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Moves files between directories on SMB share.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-MoveFiles)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, List&lt;FileItem&gt; Files, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result MoveFiles(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        try
        {
            PathString.Setup(connection.OperatingSystem);
            PathString sourcePath = input.SourcePath ?? string.Empty;
            PathString targetPath = input.TargetPath ?? string.Empty;

            return ExecuteMove(sourcePath, targetPath, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static Result ExecuteMove(
        PathString sourcePath,
        PathString targetPath,
        Connection connection,
        Options options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));

        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));

        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("TargetPath cannot be empty.", nameof(targetPath));

        if (sourcePath.Value.StartsWith($"{PathString.GetSeparatorChar()}{PathString.GetSeparatorChar()}"))
            throw new ArgumentException("SourcePath should be relative to the share, not a full UNC path.");

        if (targetPath.Value.StartsWith($"{PathString.GetSeparatorChar()}{PathString.GetSeparatorChar()}"))
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

                var filesToMove = FindMatchingFiles(fileStore, sourcePath, options, cancellationToken);

                if (filesToMove.Count == 0)
                {
                    throw new Exception($"No files found matching path '{sourcePath}'" +
                                        (string.IsNullOrWhiteSpace(options.Pattern)
                                            ? string.Empty
                                            : $" with pattern '{options.Pattern}'"));
                }

                var fileTransferEntries = BuildFileTransferEntries(
                    filesToMove,
                    sourcePath,
                    targetPath,
                    options.PreserveDirectoryStructure);

                var movedFiles = new List<FileItem>();
                var backups = new List<FileBackup>();

                try
                {
                    foreach (var kvp in fileTransferEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        PathString sourceFilePath = kvp.Key;
                        PathString targetFilePath = kvp.Value;

                        if (options.CreateTargetDirectories)
                        {
                            PathString targetDir = GetDirectoryPath(targetFilePath);

                            if (!string.IsNullOrEmpty(targetDir))
                            {
                                EnsureDirectoryExists(fileStore, targetDir);
                            }
                        }

                        PathString finalTargetPath =
                            HandleExistingFile(fileStore, targetFilePath, options.IfTargetFileExists);

                        if (finalTargetPath == targetFilePath && FileExists(fileStore, finalTargetPath))
                        {
                            PathString backupPath = finalTargetPath + ".backup_" + Guid.NewGuid().ToString("N");
                            CopyFile(fileStore, finalTargetPath, backupPath, cancellationToken);
                            backups.Add(new FileBackup
                            {
                                OriginalPath = finalTargetPath,
                                BackupPath = backupPath,
                            });
                        }

                        CopyFile(fileStore, sourceFilePath, finalTargetPath, cancellationToken);

                        movedFiles.Add(new FileItem
                        {
                            SourcePath = sourceFilePath,
                            TargetPath = finalTargetPath,
                        });
                    }

                    DeleteExistingFiles(fileStore, backups.Select(b => b.BackupPath).ToList());
                }
                catch (Exception)
                {
                    RollbackWithBackups(fileStore, movedFiles, backups, cancellationToken);

                    throw;
                }

                DeleteExistingFiles(fileStore, movedFiles.Select(x => x.SourcePath).ToList());

                return new Result
                {
                    Success = true,
                    Files = movedFiles,
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

    private static Dictionary<PathString, PathString> BuildFileTransferEntries(
        List<PathString> sourceFiles,
        PathString sourcePath,
        PathString targetPath,
        bool preserveDirectoryStructure)
    {
        var entries = new Dictionary<PathString, PathString>();

        PathString normalizedSourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : sourcePath.Value.Trim(PathString.GetSeparatorChar());
        PathString normalizedTargetPath = targetPath.Value.Trim(PathString.GetSeparatorChar());

        foreach (var sourceFile in sourceFiles)
        {
            PathString normalizedSource = sourceFile.Value.TrimStart(PathString.GetSeparatorChar());
            PathString fileName = GetFileNameFromPath(normalizedSource);

            if (!preserveDirectoryStructure)
            {
                PathString targetFile = Path.Join(normalizedTargetPath, fileName);
                entries[sourceFile] = targetFile;

                continue;
            }

            PathString relativePath;

            if (string.IsNullOrEmpty(normalizedSourcePath))
            {
                relativePath = normalizedSource;
            }
            else if (normalizedSource.Value.StartsWith(normalizedSourcePath + PathString.GetSeparatorChar(), StringComparison.OrdinalIgnoreCase))
            {
                relativePath = normalizedSource.Value.Substring(normalizedSourcePath.Value.Length).TrimStart(PathString.GetSeparatorChar());
            }
            else
            {
                relativePath = fileName;
            }

            PathString finalTarget = Path.Combine(normalizedTargetPath, relativePath);

            entries[sourceFile] = finalTarget;
        }

        return entries;
    }

    private static void DeleteExistingFiles(ISMBFileStore fileStore, List<PathString> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            PathString normalizedPath = filePath.Value.TrimStart(PathString.GetSeparatorChar());
            DeleteFile(fileStore, normalizedPath);
        }
    }

    private static PathString GetDirectoryPath(PathString filePath)
    {
        PathString normalized = filePath;
        int lastSlash = normalized.Value.LastIndexOf(PathString.GetSeparatorChar());

        return lastSlash > 0 ? normalized.Value.Substring(0, lastSlash) : string.Empty;
    }

    private static void EnsureDirectoryExists(ISMBFileStore fileStore, PathString directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
            return;

        NTStatus checkStatus = fileStore.CreateFile(
            out object checkHandle,
            out _,
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

        PathString parentDir = GetDirectoryPath(directoryPath);

        if (!string.IsNullOrEmpty(parentDir))
        {
            EnsureDirectoryExists(fileStore, parentDir);
        }

        NTStatus createStatus = fileStore.CreateFile(
            out object dirHandle,
            out _,
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

    private static PathString HandleExistingFile(
        ISMBFileStore fileStore,
        PathString targetFilePath,
        FileExistsAction action)
    {
        NTStatus checkStatus = fileStore.CreateFile(
            out object checkHandle,
            out _,
            targetFilePath,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        bool fileExists = checkStatus == NTStatus.STATUS_SUCCESS;

        if (fileExists)
        {
            fileStore.CloseFile(checkHandle);
        }

        if (!fileExists)
        {
            return targetFilePath;
        }

        switch (action)
        {
            case FileExistsAction.Throw:
                throw new IOException($"File '{targetFilePath}' already exists. No files moved.");

            case FileExistsAction.Overwrite:
                return targetFilePath;

            case FileExistsAction.Rename:
                return GenerateUniqueFilePath(fileStore, targetFilePath);

            default:
                throw new ArgumentException($"Unknown FileExistsAction: {action}");
        }
    }

    private static PathString GenerateUniqueFilePath(ISMBFileStore fileStore, PathString targetFilePath)
    {
        PathString directory = GetDirectoryPath(targetFilePath);

        PathString baseFileName = GetFileNameWithoutExtension(targetFilePath);
        PathString extension = GetFileExtension(targetFilePath);

        int counter = 1;

        do
        {
            PathString newFileName = $"{baseFileName}({counter}){extension}";
            PathString newPath = string.IsNullOrEmpty(directory)
                ? newFileName
                : $"{directory}\\{newFileName}";

            NTStatus checkStatus = fileStore.CreateFile(
                out object checkHandle,
                out _,
                newPath,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (checkStatus != NTStatus.STATUS_SUCCESS)
            {
                return newPath;
            }

            fileStore.CloseFile(checkHandle);
            counter++;
        }
        while (counter < 10000);

        throw new Exception($"Could not generate unique filename for: {targetFilePath}");
    }

    private static PathString GetFileNameFromPath(PathString path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        int lastSlash = path.Value.LastIndexOf(PathString.GetSeparatorChar());

        return lastSlash >= 0 ? path.Value.Substring(lastSlash + 1) : path;
    }

    private static PathString GetFileNameWithoutExtension(PathString path)
    {
        PathString fileName = GetFileNameFromPath(path);
        int lastDot = fileName.Value.LastIndexOf('.');

        return lastDot > 0 ? fileName.Value.Substring(0, lastDot) : fileName;
    }

    private static PathString GetFileExtension(PathString path)
    {
        PathString fileName = GetFileNameFromPath(path);
        int lastDot = fileName.Value.LastIndexOf('.');

        return lastDot >= 0 ? fileName.Value.Substring(lastDot) : string.Empty;
    }

    private static void DeleteFile(ISMBFileStore fileStore, PathString filePath)
    {
        NTStatus openStatus = fileStore.CreateFile(
            out object fileHandle,
            out _,
            filePath,
            AccessMask.DELETE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (openStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open file for deletion '{filePath}': {openStatus}");

        try
        {
            var deleteInfo = new FileDispositionInformation
            {
                DeletePending = true,
            };
            NTStatus deleteStatus = fileStore.SetFileInformation(fileHandle, deleteInfo);

            if (deleteStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to delete file '{filePath}': {deleteStatus}");
        }
        finally
        {
            fileStore.CloseFile(fileHandle);
        }
    }

    private static void CopyFile(
        ISMBFileStore fileStore,
        PathString sourceFilePath,
        PathString targetFilePath,
        CancellationToken cancellationToken)
    {
        NTStatus openSourceStatus = fileStore.CreateFile(
            out object sourceHandle,
            out _,
            sourceFilePath,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (openSourceStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open source file '{sourceFilePath}': {openSourceStatus}");

        try
        {
            NTStatus openTargetStatus = fileStore.CreateFile(
                out object targetHandle,
                out _,
                targetFilePath,
                AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Delete,
                CreateDisposition.FILE_OVERWRITE_IF,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (openTargetStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to create target file '{targetFilePath}': {openTargetStatus}");

            try
            {
                NTStatus infoStatus = fileStore.GetFileInformation(
                    out FileInformation fileInfo,
                    sourceHandle,
                    FileInformationClass.FileStandardInformation);

                if (infoStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception($"Failed to get file information for '{sourceFilePath}': {infoStatus}");

                var standardInfo = (FileStandardInformation)fileInfo;
                long fileSize = standardInfo.EndOfFile;
                long bytesRead = 0;
                const int bufferSize = 65536;

                while (bytesRead < fileSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int readSize = (int)Math.Min(bufferSize, fileSize - bytesRead);

                    NTStatus readStatus = fileStore.ReadFile(out byte[] data, sourceHandle, bytesRead, readSize);

                    if (readStatus != NTStatus.STATUS_SUCCESS && readStatus != NTStatus.STATUS_END_OF_FILE)
                        throw new Exception($"Failed to read from file '{sourceFilePath}': {readStatus}");

                    if (data == null || data.Length == 0)
                        break;

                    NTStatus writeStatus = fileStore.WriteFile(out int bytesWritten, targetHandle, bytesRead, data);

                    if (writeStatus != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Failed to write to file '{targetFilePath}': {writeStatus}");

                    if (bytesWritten != data.Length)
                    {
                        throw new Exception(
                            $"Partial write detected for '{targetFilePath}': wrote {bytesWritten} of {data.Length} bytes.");
                    }

                    bytesRead += data.Length;
                }
            }
            catch
            {
                DeleteFile(fileStore, targetFilePath);

                throw;
            }
            finally
            {
                fileStore.CloseFile(targetHandle);
            }
        }
        finally
        {
            fileStore.CloseFile(sourceHandle);
        }
    }

    private static List<PathString> FindMatchingFiles(
        ISMBFileStore fileStore,
        PathString basePath,
        Options options,
        CancellationToken cancellationToken)
    {
        basePath = basePath?.Value.Trim(PathString.GetSeparatorChar()) ?? string.Empty;

        if (!string.IsNullOrEmpty(basePath))
        {
            NTStatus fileCheckStatus = fileStore.CreateFile(
                out object fileHandle,
                out _,
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

                return [basePath.Value.TrimStart(PathString.GetSeparatorChar())];
            }
        }

        var regex = PrepareRegex(options);

        List<PathString> matchedFiles =
            EnumerateFiles(fileStore, basePath, regex, options.Recursive, cancellationToken);

        return matchedFiles;
    }

    private static List<PathString> EnumerateFiles(
        ISMBFileStore fileStore,
        PathString directoryPath,
        Regex regex,
        bool recursive,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(directoryPath) || directoryPath == "\\" || directoryPath == "/")
        {
            directoryPath = string.Empty;
        }
        else
        {
            directoryPath = directoryPath.Value.Trim(PathString.GetSeparatorChar());
        }

        NTStatus openStatus = fileStore.CreateFile(
            out object dirHandle,
            out _,
            directoryPath,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (openStatus != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open directory '{directoryPath}': {openStatus}");

        var files = new List<PathString>();

        try
        {
            NTStatus status;

            do
            {
                token.ThrowIfCancellationRequested();

                status = fileStore.QueryDirectory(
                    out var entries,
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

                    PathString fileName = entry.FileName;

                    if (fileName == "." || fileName == "..")
                        continue;

                    PathString fullPath = string.IsNullOrEmpty(directoryPath)
                        ? fileName
                        : $"{directoryPath}\\{fileName}";

                    bool isDir = (entry.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;

                    if (isDir && recursive)
                    {
                        var subDirFiles = EnumerateFiles(fileStore, fullPath, regex, true, token);
                        files.AddRange(subDirFiles);
                    }
                    else if (!isDir)
                    {
                        if (regex == null || regex.IsMatch(fileName))
                        {
                            files.Add(fullPath);
                        }
                    }
                }
            }
            while (status == NTStatus.STATUS_SUCCESS);
        }
        finally
        {
            fileStore.CloseFile(dirHandle);
        }

        return files;
    }

    private static bool FileExists(ISMBFileStore fileStore, PathString filePath)
    {
        NTStatus checkStatus = fileStore.CreateFile(
            out object checkHandle,
            out _,
            filePath,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (checkStatus == NTStatus.STATUS_SUCCESS)
        {
            fileStore.CloseFile(checkHandle);

            return true;
        }

        return false;
    }

    private static Regex PrepareRegex(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Pattern)) return null;
        var pattern = options.Pattern;
        if (options.PatternMatchingMode == PatternMatchingMode.Wildcards)
            pattern = "^" + Regex.Escape(options.Pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";

        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static void RollbackWithBackups(
        ISMBFileStore fileStore,
        List<FileItem> movedFiles,
        List<FileBackup> backups,
        CancellationToken cancellationToken)
    {
        foreach (var backup in backups)
        {
            try
            {
                if (FileExists(fileStore, backup.OriginalPath))
                {
                    DeleteFile(fileStore, backup.OriginalPath);
                }

                CopyFile(fileStore, backup.BackupPath, backup.OriginalPath, cancellationToken);

                DeleteFile(fileStore, backup.BackupPath);
            }
            catch
            {
                // Swallow exceptions to continue cleanup. The main operation exception
                // will be thrown anyway, so partial rollback failures
                // won't mask the original error.
            }
        }

        var backedUpPaths = new HashSet<PathString>(backups.Select(b => b.OriginalPath));

        foreach (var file in movedFiles)
        {
            PathString normalizedPath = file.TargetPath;

            if (!backedUpPaths.Contains(normalizedPath))
            {
                try
                {
                    DeleteFile(fileStore, file.TargetPath);
                }
                catch
                {
                    // Swallow exceptions to continue cleanup. The main operation exception
                    // will be thrown anyway, so partial rollback failures
                    // won't mask the original error.
                }
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
