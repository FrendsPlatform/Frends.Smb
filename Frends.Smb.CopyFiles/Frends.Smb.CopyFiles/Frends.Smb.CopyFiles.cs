using System;
using System.ComponentModel;
using System.Threading;
using Frends.Smb.CopyFiles.Definitions;
using Frends.Smb.CopyFiles.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.CopyFiles;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Task for copying files inside SMB share.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-CopyFiles)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, List&lt;FileItem&gt; Files, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result CopyFiles(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        SMB2Client srcClient = null;
        SMB2Client dstClient = null;

        ISMBFileStore dstFileStore = null;
        ISMBFileStore srcFileStore = null;
        try
        {
            SmbHandler.ValidateParameters(input, connection);
            SmbHandler.PrepareSmbConnection(out dstClient, out dstFileStore, connection);
            SmbHandler.PrepareSmbConnection(out srcClient, out srcFileStore, connection);
            input.SourcePath = input.SourcePath.ToSmbPath();
            input.TargetPath = input.TargetPath.ToSmbPath();
            int maxChunkSize;
            try
            {
                maxChunkSize = srcClient.MaxReadSize < dstClient.MaxWriteSize
                    ? checked((int)srcClient.MaxReadSize)
                    : checked((int)dstClient.MaxWriteSize);
            }
            catch
            {
                maxChunkSize = 64 * 1024; // 64KB
            }

            var copiedFiles = SmbHandler.CopyFiles(
                dstFileStore,
                srcFileStore,
                input,
                options,
                maxChunkSize,
                cancellationToken);

            return new Result { Success = true, Files = copiedFiles, Error = null, };
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
        finally
        {
            dstFileStore?.Disconnect();
            srcFileStore?.Disconnect();

            NTStatus status = NTStatus.STATUS_NOT_IMPLEMENTED;
            try
            {
                srcClient?.ListShares(out status);
                dstClient?.ListShares(out status);
            }
            catch
            {
                // ignored
            }

            if (status == NTStatus.STATUS_SUCCESS)
            {
                srcClient?.Logoff();
                dstClient?.Logoff();
            }

            srcClient?.Disconnect();
            dstClient?.Disconnect();
        }
    }
}
