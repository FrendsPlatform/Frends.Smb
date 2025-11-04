using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.ReadFile.Definitions;
using Frends.Smb.ReadFile.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.ReadFile;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Reads a file from directory.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-ReadFile)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string Content, string Path, double SizeInMegaBytes, DateTime CreationTime, DateTime LastWriteTime, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static async Task<Result> ReadFile(
    [PropertyTab] Input input,
    [PropertyTab] Connection connection,
    [PropertyTab] Options options,
    CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteReadAsync(input, connection, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }

    private static async Task<Result> ExecuteReadAsync(
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

        if (input.Path.StartsWith(@"\\"))
            throw new ArgumentException("Path should be relative to the share, not a full UNC path.");

        Encoding encoding = GetEncoding(options.FileEncoding, options.EnableBom, options.EncodingInString);

        var (domain, user) = GetDomainAndUsername(connection.UserName);

        SMB2Client client = new();

        try
        {
            bool connected = client.Connect(connection.Server, SMBTransportType.DirectTCPTransport);
            if (!connected)
                throw new Exception($"Failed to connect to SMB server: {connection.Server}");

            cancellationToken.ThrowIfCancellationRequested();

            NTStatus loginStatus = client.Login(domain, user, connection.Password);
            if (loginStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"SMB login failed: {loginStatus}");

            ISMBFileStore fileStore = client.TreeConnect(connection.Share, out NTStatus treeStatus);
            if (treeStatus != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to connect to share '{connection.Share}': {treeStatus}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                NTStatus openStatus = fileStore.CreateFile(
                    out object fileHandle,
                    out FileStatus fileStatus,
                    input.Path,
                    AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                    SMBLibrary.FileAttributes.Normal,
                    ShareAccess.Read,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                    null);

                if (openStatus != NTStatus.STATUS_SUCCESS)
                    throw new Exception($"Failed to open file '{input.Path}': {openStatus}, file status: {fileStatus}");

                try
                {
                    NTStatus infoStatus = fileStore.GetFileInformation(
                        out FileInformation fileInfo,
                        fileHandle,
                        FileInformationClass.FileBasicInformation);

                    if (infoStatus != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Failed to get file information: {infoStatus}");

                    var basicInfo = (FileBasicInformation)fileInfo;
                    DateTime? creationTime = basicInfo.CreationTime.Time;
                    DateTime? lastWriteTime = basicInfo.LastWriteTime.Time;

                    NTStatus sizeStatus = fileStore.GetFileInformation(
                        out FileInformation sizeInfo,
                        fileHandle,
                        FileInformationClass.FileStandardInformation);

                    if (sizeStatus != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Failed to get file size: {sizeStatus}");

                    var standardInfo = (FileStandardInformation)sizeInfo;
                    long fileSize = standardInfo.EndOfFile;

                    cancellationToken.ThrowIfCancellationRequested();

                    using var memoryStream = new MemoryStream();
                    long bytesRead = 0;
                    int maxReadSize = (int)client.MaxReadSize;

                    while (bytesRead < fileSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int readSize = (int)Math.Min(maxReadSize, fileSize - bytesRead);

                        NTStatus readStatus = fileStore.ReadFile(
                            out byte[] buffer,
                            fileHandle,
                            bytesRead,
                            readSize);

                        if (readStatus != NTStatus.STATUS_SUCCESS && readStatus != NTStatus.STATUS_END_OF_FILE)
                            throw new Exception($"Failed to read file: {readStatus}");

                        if (buffer == null || buffer.Length == 0)
                            break;

                        await memoryStream.WriteAsync(buffer, cancellationToken);
                        bytesRead += buffer.Length;

                        if (readStatus == NTStatus.STATUS_END_OF_FILE)
                            break;
                    }

                    byte[] content = memoryStream.ToArray();
                    double sizeInMb = fileSize / (1024.0 * 1024.0);

                    return new Result
                    {
                        Success = true,
                        Content = content,
                        Path = input.Path,
                        SizeInMegaBytes = sizeInMb,
                        CreationTime = creationTime,
                        LastWriteTime = lastWriteTime,
                        Error = null,
                    };
                }
                finally
                {
                    fileStore.CloseFile(fileHandle);
                }
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

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        if (domainAndUserName.Length != 2)
            throw new ArgumentException($@"UserName field must be of format domain\username was: {username}");
        return new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }

    private static Encoding GetEncoding(FileEncoding fileEncoding, bool enableBom, string encodingInString)
    {
        switch (fileEncoding)
        {
            case FileEncoding.Other:
                return Encoding.GetEncoding(encodingInString);
            case FileEncoding.Ascii:
                return Encoding.ASCII;
            case FileEncoding.Default:
                return Encoding.Default;
            case FileEncoding.Utf8:
                return enableBom ? new UTF8Encoding(true) : new UTF8Encoding(false);
            case FileEncoding.Windows1252:
                EncodingProvider provider = CodePagesEncodingProvider.Instance;
                Encoding.RegisterProvider(provider);
                return Encoding.GetEncoding(1252);
            case FileEncoding.Unicode:
                return Encoding.Unicode;
            default:
                throw new ArgumentOutOfRangeException(nameof(fileEncoding), fileEncoding, "Unsupported file encoding value.");
        }
    }
}
