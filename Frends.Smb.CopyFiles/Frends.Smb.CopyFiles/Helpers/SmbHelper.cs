using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Frends.Smb.CopyFiles.Definitions;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Frends.Smb.CopyFiles.Helpers;

internal static class SmbHandler
{
    internal static void ValidateParameters(PathString sourcePath, PathString targetPath, Connection connection)
    {
        if (sourcePath == targetPath)
            throw new ArgumentException("Destination and source cannot be the same");
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server cannot be empty.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Share))
            throw new ArgumentException("Share cannot be empty.", nameof(connection));
        if (sourcePath.Value.StartsWith($"{PathString.GetSeparatorChar()}{PathString.GetSeparatorChar()}"))
            throw new ArgumentException("SourcePath should be relative to the share, not a full UNC path.");
        if (targetPath.Value.StartsWith($"{PathString.GetSeparatorChar()}{PathString.GetSeparatorChar()}"))
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

    internal static List<FileItem> CopyFiles(
        ISMBFileStore dstFileStore,
        ISMBFileStore srcFileStore,
        PathString sourcePath,
        PathString targetPath,
        Options options,
        int maxChunkSize,
        CancellationToken cancellationToken)
    {
        var result = new List<FileItem>();
        var sources = GetSourceFiles(
            srcFileStore,
            sourcePath,
            options.Recursive,
            options.Pattern,
            options.PatternMatchingMode,
            cancellationToken);
        var newlyCreatedFiles = new List<PathString>();
        var tempFiles = new List<Tuple<PathString, PathString>>();

        try
        {
            foreach (var source in sources)
            {
                var dstPath = PrepareDestinationPath(source, targetPath, options.PreserveDirectoryStructure);
                var finalDstPath = PrepareForCopy(
                    dstPath,
                    dstFileStore,
                    options.IfTargetFileExists,
                    options.CreateTargetDirectories,
                    ref newlyCreatedFiles,
                    ref tempFiles);

                // simple copy should work now as all is prepared without exceptions
                SafeCopy(source.FilePath, srcFileStore, finalDstPath, dstFileStore, maxChunkSize);
                result.Add(new FileItem { SourcePath = source.FilePath, TargetPath = finalDstPath });
            }
        }
        catch
        {
            // remove newly created files
            foreach (var newFile in newlyCreatedFiles)
            {
                dstFileStore.CreateFile(
                    out var handle,
                    out _,
                    newFile,
                    AccessMask.DELETE,
                    FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE,
                    null);
                FileDispositionInformation fileDispositionInformation =
                    new FileDispositionInformation { DeletePending = true };
                dstFileStore.SetFileInformation(handle, fileDispositionInformation);
                dstFileStore.CloseFile(handle);
            }

            // roll back temp files
            foreach (var (tmpFile, orgFile) in tempFiles)
            {
                dstFileStore.CreateFile(
                    out var dstHandle,
                    out _,
                    tmpFile,
                    AccessMask.GENERIC_WRITE,
                    FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE,
                    null);
                var rename = new FileRenameInformationType2 { ReplaceIfExists = true, FileName = orgFile };
                dstFileStore.SetFileInformation(dstHandle, rename);
                dstFileStore.CloseFile(dstHandle);
            }

            throw;
        }

        // remove temporary files
        foreach (var (tmpFile, _) in tempFiles)
        {
            dstFileStore.CreateFile(
                out var handle,
                out _,
                tmpFile,
                AccessMask.DELETE,
                FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);
            FileDispositionInformation fileDispositionInformation =
                new FileDispositionInformation { DeletePending = true };
            dstFileStore.SetFileInformation(handle, fileDispositionInformation);
            dstFileStore.CloseFile(handle);
        }

        return result;
    }

    private static void SafeCopy(
        PathString srcPath,
        ISMBFileStore srcFileStore,
        PathString dstPath,
        ISMBFileStore dstFileStore,
        int maxChunkSize)
    {
        var srcStatus = srcFileStore.CreateFile(
            out var srcHandle,
            out _,
            srcPath,
            AccessMask.GENERIC_READ,
            FileAttributes.Normal,
            ShareAccess.Read,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);
        var dstStatus = dstFileStore.CreateFile(
            out var dstHandle,
            out _,
            dstPath,
            AccessMask.GENERIC_WRITE,
            FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (srcStatus != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to open source file: {srcPath}");
        if (dstStatus != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to open destination file: {dstPath}");

        try
        {
            long bytesRead = 0;

            while (true)
            {
                srcStatus = srcFileStore.ReadFile(out var data, srcHandle, bytesRead, maxChunkSize);

                if (srcStatus != NTStatus.STATUS_SUCCESS && srcStatus != NTStatus.STATUS_END_OF_FILE)
                    throw new Exception("Failed to read from source file");

                if (srcStatus == NTStatus.STATUS_END_OF_FILE || data.Length == 0) break;
                dstStatus = dstFileStore.WriteFile(out _, dstHandle, bytesRead, data);
                bytesRead += data.Length;

                if (dstStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception("Failed to write to file");
            }
        }
        finally
        {
            srcFileStore.CloseFile(srcHandle);
            dstFileStore.CloseFile(dstHandle);
        }
    }

    private static PathString PrepareForCopy(
        PathString dstPath,
        ISMBFileStore dstStore,
        FileExistsAction fileExistsAction,
        bool createTargetDirectories,
        ref List<PathString> newlyCreatedFiles,
        ref List<Tuple<PathString, PathString>> tempFiles)
    {
        PathString finalDstPath = dstPath;
        NTStatus status = dstStore.CreateFile(
            out var dstHandle,
            out _,
            dstPath,
            AccessMask.GENERIC_WRITE,
            FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (status == NTStatus.STATUS_SUCCESS)
        {
            switch (fileExistsAction)
            {
                case FileExistsAction.Overwrite:
                    PathString dstFileName = Path.GetFileName(dstPath);
                    PathString tempName = Path.Combine(
                        Path.GetDirectoryName(dstPath) ?? string.Empty,
                        $"temp-{Guid.NewGuid().ToString()}-{dstFileName}");
                    var rename = new FileRenameInformationType2 { ReplaceIfExists = false, FileName = tempName };
                    status = dstStore.SetFileInformation(dstHandle, rename);

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new Exception(
                            $"Failed to rename existing file {dstPath} to temporary name {tempName} before overwriting. Status: {status}");
                    }

                    dstStore.CloseFile(dstHandle);
                    tempFiles.Add(new Tuple<PathString, PathString>(tempName, dstPath));

                    break;
                case FileExistsAction.Rename:
                    dstStore.CloseFile(dstHandle);
                    finalDstPath = GenerateUniqueFilePath(dstStore, dstPath);
                    newlyCreatedFiles.Add(finalDstPath);

                    break;
                case FileExistsAction.Throw:
                    dstStore.CloseFile(dstHandle);

                    throw new Exception($"File {dstPath} already exists.");
                default:
                    dstStore.CloseFile(dstHandle);

                    throw new ArgumentOutOfRangeException(
                        nameof(fileExistsAction),
                        "Unknown IfTargetFileExists value.");
            }
        }

        if (status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
        {
            if (createTargetDirectories) EnsureDirectoriesExist(dstStore, dstPath);
            else
                throw new Exception("Target directory does not exist and Options.CreateTargetDirectories is disabled.");
        }

        status = dstStore.CreateFile(
            out _,
            out _,
            finalDstPath,
            AccessMask.GENERIC_WRITE,
            FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        return status != NTStatus.STATUS_SUCCESS
            ? throw new Exception("Failed to prepare target file to write into")
            : finalDstPath;
    }

    private static PathString GenerateUniqueFilePath(ISMBFileStore fileStore, PathString path)
    {
        PathString directory = Path.GetDirectoryName(path);
        PathString baseFileName = Path.GetFileNameWithoutExtension(path);
        PathString extension = Path.GetExtension(path);

        int counter = 1;

        while (counter < 10000)
        {
            PathString newFileName = $"{baseFileName}({counter}){extension}";
            PathString newPath = string.IsNullOrEmpty(directory)
                ? newFileName
                : $"{directory}{PathString.GetSeparatorChar()}{newFileName}";

            NTStatus checkStatus = fileStore.CreateFile(
                out object checkHandle,
                out _,
                newPath,
                AccessMask.GENERIC_READ,
                FileAttributes.Normal,
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

        throw new Exception($"Could not generate unique filename for: {path}");
    }

    private static void EnsureDirectoriesExist(ISMBFileStore fileStore, PathString smbFullPath)
    {
        ArgumentNullException.ThrowIfNull(fileStore);

        if (string.IsNullOrWhiteSpace(smbFullPath)) return;

        PathString directory = Path.GetDirectoryName(smbFullPath);

        if (string.IsNullOrEmpty(directory)) return;

        var parts = directory.Value.Split([PathString.GetSeparatorChar()], StringSplitOptions.RemoveEmptyEntries);

        PathString current = string.Empty;

        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? part : $"{current}{PathString.GetSeparatorChar()}{part}";

            var status = fileStore.CreateFile(
                out _,
                out _,
                current,
                AccessMask.SYNCHRONIZE | AccessMask.GENERIC_WRITE,
                FileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write,
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

    private static PathString PrepareDestinationPath(
        SourceFileInfo sourceFile,
        PathString destinationRoot,
        bool preserveStructure)
    {
        PathString pathSuffix = preserveStructure
            ? sourceFile.RelativeFilePath.Value
            : Path.GetFileName(sourceFile.FilePath);

        return Path.Combine(destinationRoot, pathSuffix);
    }

    private static List<SourceFileInfo> GetSourceFiles(
        ISMBFileStore fileStore,
        PathString sourcePath,
        bool recursive,
        string pattern,
        PatternMatchingMode patternMatchingMode,
        CancellationToken cancellationToken)
    {
        var result = new List<SourceFileInfo>();

        // sourcePath is a file
        if (TryOpenFileToRead(fileStore, sourcePath, out object fileHandle))
        {
            fileStore.CloseFile(fileHandle);
            if (MatchesPattern(Path.GetFileName(sourcePath), pattern, patternMatchingMode))
                result.Add(new SourceFileInfo(sourcePath, sourcePath));

            return result;
        }

        // sourcePath is a directory
        CollectFiles(
            fileStore,
            sourcePath,
            recursive,
            pattern,
            patternMatchingMode,
            result,
            sourcePath,
            cancellationToken);

        return result;
    }

    private static void CollectFiles(
        ISMBFileStore fileStore,
        PathString directoryPath,
        bool recursive,
        string pattern,
        PatternMatchingMode mode,
        List<SourceFileInfo> output,
        PathString initialSourcePath,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!TryOpenFileToRead(fileStore, directoryPath, out object dirHandle, isDirectory: true))
            return;

        try
        {
            NTStatus status = fileStore.QueryDirectory(
                out List<QueryDirectoryFileInformation> entries,
                dirHandle,
                "*",
                FileInformationClass.FileDirectoryInformation);

            if (status != NTStatus.STATUS_NO_MORE_FILES)
                return;

            foreach (var e in entries.Cast<FileDirectoryInformation>())
            {
                token.ThrowIfCancellationRequested();

                PathString name = e.FileName;

                if (name == "." || name == "..")
                    continue;

                PathString fullPath = Path.Combine(directoryPath, name);

                if (IsDirectory(e) && recursive)
                    CollectFiles(fileStore, fullPath, true, pattern, mode, output, initialSourcePath, token);
                if (!IsDirectory(e) && MatchesPattern(name, pattern, mode))
                    output.Add(new SourceFileInfo(fullPath, initialSourcePath));
            }
        }
        finally
        {
            fileStore.CloseFile(dirHandle);
        }
    }

    private static bool TryOpenFileToRead(
        ISMBFileStore store,
        PathString path,
        out object handle,
        bool isDirectory = false)
    {
        NTStatus status = store.CreateFile(
            out handle,
            out _,
            path,
            AccessMask.GENERIC_READ,
            isDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            isDirectory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        if (status == NTStatus.STATUS_SUCCESS) return true;

        handle = null;

        return false;
    }

    private static bool MatchesPattern(PathString fileName, string pattern, PatternMatchingMode mode)
    {
        var regexPattern = mode switch
        {
            PatternMatchingMode.Wildcards => WildcardPattern(pattern),
            PatternMatchingMode.Regex => pattern,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(regexPattern) || Regex.IsMatch(
            fileName,
            regexPattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static string WildcardPattern(string pattern) =>
        "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";

    private static bool IsDirectory(FileDirectoryInformation info) =>
        (info.FileAttributes & FileAttributes.Directory) != 0;

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');

        return domainAndUserName.Length != 2
            ? throw new ArgumentException($@"Username field must be of format domain\username was: {username}")
            : new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }
}
