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
            return ExecuteMove(input, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static Result ExecuteMove(
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

                var filesToMove = FindMatchingFiles(fileStore, input.SourcePath, options, cancellationToken);

                if (filesToMove.Count == 0)
                {
                    throw new Exception($"No files found matching path '{input.SourcePath}'" +
                        (string.IsNullOrWhiteSpace(options.Pattern) ? string.Empty : $" with pattern '{options.Pattern}'"));
                }

                var fileTransferEntries = BuildFileTransferEntries(
                    filesToMove,
                    input.SourcePath,
                    input.TargetPath,
                    options.PreserveDirectoryStructure);

                if (options.CreateTargetDirectories && !string.IsNullOrEmpty(input.TargetPath))
                {
                    EnsureDirectoryExists(fileStore, input.TargetPath.Trim('\\', '/'));
                }

                var movedFiles = new List<FileItem>();

                try
                {
                    foreach (var kvp in fileTransferEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string sourceFilePath = kvp.Key;
                        string targetFilePath = kvp.Value;

                        if (options.CreateTargetDirectories)
                        {
                            string targetDir = GetDirectoryPath(targetFilePath);
                            if (!string.IsNullOrEmpty(targetDir))
                            {
                                EnsureDirectoryExists(fileStore, targetDir);
                            }
                        }

                        string finalTargetPath = HandleExistingFile(fileStore, targetFilePath, options.IfTargetFileExists);

                        CopyFile(fileStore, sourceFilePath, finalTargetPath, cancellationToken);

                        movedFiles.Add(new FileItem
                        {
                            SourcePath = sourceFilePath.Replace('/', '\\'),
                            TargetPath = finalTargetPath.Replace('/', '\\'),
                        });
                    }
                }
                catch (Exception)
                {
                    DeleteExistingFiles(fileStore, movedFiles.Select(x => x.TargetPath).ToList());
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

    private static Dictionary<string, string> BuildFileTransferEntries(
    List<string> sourceFiles,
    string sourcePath,
    string targetPath,
    bool preserveDirectoryStructure)
    {
        var entries = new Dictionary<string, string>();

        string normalizedSourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : sourcePath.Replace('/', '\\').Trim('\\');
        string normalizedTargetPath = targetPath.Replace('/', '\\').Trim('\\');

        // DEBUG: Log the inputs
        Console.WriteLine($"[DEBUG] BuildFileTransferEntries:");
        Console.WriteLine($"[DEBUG]   sourcePath: '{sourcePath}'");
        Console.WriteLine($"[DEBUG]   normalizedSourcePath: '{normalizedSourcePath}'");
        Console.WriteLine($"[DEBUG]   targetPath: '{targetPath}'");
        Console.WriteLine($"[DEBUG]   preserveDirectoryStructure: {preserveDirectoryStructure}");
        Console.WriteLine($"[DEBUG]   sourceFiles count: {sourceFiles.Count}");

        foreach (var sourceFile in sourceFiles)
        {
            string normalizedSource = sourceFile.Replace('/', '\\').TrimStart('\\');
            string fileName = GetFileNameFromPath(normalizedSource);

            Console.WriteLine($"[DEBUG]   Processing: '{sourceFile}' -> normalized: '{normalizedSource}', fileName: '{fileName}'");

            if (!preserveDirectoryStructure)
            {
                string targetFile = Path.Join(normalizedTargetPath, fileName);
                entries[sourceFile] = targetFile;
                Console.WriteLine($"[DEBUG]     No preserve -> target: '{targetFile}'");
                continue;
            }

            string relativePath;

            if (string.IsNullOrEmpty(normalizedSourcePath))
            {
                relativePath = normalizedSource;
                Console.WriteLine($"[DEBUG]     Empty source -> relativePath: '{relativePath}'");
            }
            else if (normalizedSource.StartsWith(normalizedSourcePath + "\\", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = normalizedSource.Substring(normalizedSourcePath.Length).TrimStart('\\');
                Console.WriteLine($"[DEBUG]     Subdirectory -> relativePath: '{relativePath}'");
            }
            else
            {
                relativePath = fileName;
                Console.WriteLine($"[DEBUG]     Fallback -> relativePath: '{relativePath}'");
            }

            string finalTarget = Path.Combine(
                normalizedTargetPath,
                relativePath.Replace('\\', Path.DirectorySeparatorChar))
            .Replace('/', Path.DirectorySeparatorChar);

            entries[sourceFile] = finalTarget;
            Console.WriteLine($"[DEBUG]     Final target: '{finalTarget}'");
        }

        return entries;
    }

    private static void DeleteExistingFiles(ISMBFileStore fileStore, List<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                string normalizedPath = filePath.Replace('/', '\\').TrimStart('\\');
                Console.WriteLine($"[DEBUG] Attempting to delete: '{filePath}' -> normalized: '{normalizedPath}'");
                DeleteFile(fileStore, normalizedPath);
                Console.WriteLine($"[DEBUG] Successfully deleted: '{filePath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Failed to delete '{filePath}': {ex.Message}");
                throw; // Re-throw to see the actual error
            }
        }
    }

    private static string GetDirectoryPath(string filePath)
    {
        string normalized = filePath.Replace('/', '\\');
        int lastSlash = normalized.LastIndexOf('\\');
        return lastSlash > 0 ? normalized.Substring(0, lastSlash) : string.Empty;
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

    private static string HandleExistingFile(
        ISMBFileStore fileStore,
        string targetFilePath,
        FileExistsAction action)
    {
        NTStatus checkStatus = fileStore.CreateFile(
            out object checkHandle,
            out FileStatus fileStatus,
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

    private static string GenerateUniqueFilePath(ISMBFileStore fileStore, string targetFilePath)
    {
        string normalized = targetFilePath.Replace('/', '\\');
        string directory = GetDirectoryPath(normalized);

        string baseFileName = GetFileNameWithoutExtension(normalized);
        string extension = GetFileExtension(normalized);

        int counter = 1;
        string newPath;

        do
        {
            string newFileName = $"{baseFileName}({counter}){extension}";
            newPath = string.IsNullOrEmpty(directory)
                ? newFileName
                : $"{directory}\\{newFileName}";

            NTStatus checkStatus = fileStore.CreateFile(
                out object checkHandle,
                out FileStatus fileStatus,
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

    private static string GetFileNameFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        int lastBackslash = path.LastIndexOf('\\');
        int lastForwardSlash = path.LastIndexOf('/');
        int lastSlash = Math.Max(lastBackslash, lastForwardSlash);

        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }

    private static string GetFileNameWithoutExtension(string path)
    {
        string fileName = GetFileNameFromPath(path);
        int lastDot = fileName.LastIndexOf('.');
        return lastDot > 0 ? fileName.Substring(0, lastDot) : fileName;
    }

    private static string GetFileExtension(string path)
    {
        string fileName = GetFileNameFromPath(path);
        int lastDot = fileName.LastIndexOf('.');
        return lastDot >= 0 ? fileName.Substring(lastDot) : string.Empty;
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

    private static void CopyFile(ISMBFileStore fileStore, string sourceFilePath, string targetFilePath, CancellationToken cancellationToken)
    {
        NTStatus openSourceStatus = fileStore.CreateFile(
            out object sourceHandle,
            out FileStatus sourceFileStatus,
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
                out FileStatus targetFileStatus,
                targetFilePath,
                AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OVERWRITE_IF,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (openTargetStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to create target file '{targetFilePath}': {openTargetStatus}");

            try
            {
                NTStatus infoStatus = fileStore.GetFileInformation(out FileInformation fileInfo, sourceHandle, FileInformationClass.FileStandardInformation);

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

                    bytesRead += bytesWritten;
                }
            }
            catch
            {
                try
                {
                    DeleteFile(fileStore, targetFilePath);
                }
                catch
                {
                }

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

    private static List<string> FindMatchingFiles(
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
                return new List<string> { basePath.Replace('/', '\\').TrimStart('\\') };
            }
        }

        var regex = PrepareRegex(options);

        matchedFiles = EnumerateFiles(fileStore, basePath, regex, options.Recursive, cancellationToken);

        Console.WriteLine($"[DEBUG] FindMatchingFiles found {matchedFiles.Count} files:");
        foreach (var file in matchedFiles)
        {
            Console.WriteLine($"[DEBUG]   '{file}'");
        }

        return matchedFiles;
    }

    private static List<string> EnumerateFiles(
     ISMBFileStore fileStore,
     string directoryPath,
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

                    string fullPath = string.IsNullOrEmpty(directoryPath)
                        ? fileName
                        : $"{directoryPath}\\{fileName}";

                    bool isDir = (entry.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;

                    if (isDir && recursive)
                    {
                        var subDirFiles = EnumerateFiles(fileStore, fullPath, regex, recursive, token);
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

    private static Regex PrepareRegex(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Pattern)) return null;
        var pattern = options.Pattern;
        if (options.PatternMatchingMode == PatternMatchingMode.Wildcards)
            pattern = "^" + Regex.Escape(options.Pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
