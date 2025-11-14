using System;
using System.ComponentModel;
using System.Threading;
using Frends.Smb.CreateDirectory.Definitions;
using Frends.Smb.CreateDirectory.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.CreateDirectory;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Task to create a directory on SMB share.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-CreateDirectory)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string FullUncPath, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result CreateDirectory(
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
            SmbHandler.CreateDirectory(fileStore, input.DirectoryPath, cancellationToken);

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
