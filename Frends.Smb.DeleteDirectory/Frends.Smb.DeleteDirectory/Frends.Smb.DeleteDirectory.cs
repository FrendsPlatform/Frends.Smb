using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Frends.Smb.DeleteDirectory.Definitions;
using Frends.Smb.DeleteDirectory.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.DeleteDirectory;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Task for deleting SMB directories.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-DeleteDirectory)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string FullUncPath, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result DeleteDirectory(
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
            SmbHandler.DeleteDirectory(fileStore, input.DirectoryPath, options.DeleteRecursively, cancellationToken);

            return new Result
            {
                Success = true,
                FullUncPath =
                    $@"\\{connection.Server}\{connection.Share}\{input.DirectoryPath.Replace('/', '\\')}",
                Error = null,
            };
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
