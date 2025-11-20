using System;
using System.ComponentModel;
using System.Linq;
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
    /// <returns>object { bool Success, List&lt;FileItem&gt; Files, object Error { string Message, Exception AdditionalInfo } }</returns>    // TODO: Remove Connection parameter if the task does not make connections
    public static Result CopyFiles(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        SMB2Client client = null;
        ISMBFileStore fileStore = null;
        try
        {
            SmbHandler.ValidateParameters(input, connection);
            SmbHandler.PrepareSmbConnection(out client, out fileStore, connection);
            var copiedFiles = SmbHandler.CopyFiles(fileStore, input, options, cancellationToken);

            return new Result { Success = true, Files = copiedFiles, Error = null, };
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
        finally
        {
            fileStore?.Disconnect();

            NTStatus status = NTStatus.STATUS_NOT_IMPLEMENTED;
            try
            {
                client?.ListShares(out status);
            }
            catch
            {
                // ignored
            }

            if (status == NTStatus.STATUS_SUCCESS) client?.Logoff();
            client?.Disconnect();
        }
    }
}
