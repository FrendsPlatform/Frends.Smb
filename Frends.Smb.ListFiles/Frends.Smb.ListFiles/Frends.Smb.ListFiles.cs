using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Frends.Smb.ListFiles.Definitions;
using Frends.Smb.ListFiles.Helpers;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.ListFiles;

/// <summary>
/// Task Class for Smb operations.
/// </summary>
public static class Smb
{
    /// <summary>
    /// Task to list files from the SMB server.
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Smb-ListFiles)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="connection">Connection parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string[] Files, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result ListFiles(
        [PropertyTab] Input input,
        [PropertyTab] Connection connection,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        var client = new SMB2Client();
        ISMBFileStore? fileStore = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(connection.Server))
                throw new ArgumentException("Server cannot be empty.", nameof(connection));
            if (string.IsNullOrWhiteSpace(connection.Share))
                throw new ArgumentException("Share cannot be empty.", nameof(connection));
            if (string.IsNullOrWhiteSpace(input.Directory))
                throw new ArgumentException("Directory cannot be empty.", nameof(input));
            if (input.Directory.StartsWith(@"\\"))
                throw new ArgumentException("Path should be relative to the share, not a full UNC path.");

            var (domain, username) = GetDomainAndUsername(connection.Username);

            if (!IPAddress.TryParse(connection.Server, out var serverAddress))
            {
                var hostEntry = Dns.GetHostEntry(connection.Server);
                serverAddress = hostEntry.AddressList.FirstOrDefault()
                                ?? throw new Exception($"Could not resolve hostname: {connection.Server}");
            }

            var isConnected = client.Connect(serverAddress, SMBTransportType.DirectTCPTransport);
            if (!isConnected) throw new Exception("Failed to connect to SMB server");
            NTStatus status = client.Login(domain, username, connection.Password);
            if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"SMB login failed: {status}");
            fileStore = client.TreeConnect(connection.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS) throw new Exception($"Failed to connect to share: {status}");

            // Normalize the base directory to SMB path format (forward slashes)
            var baseDir = input.Directory.Replace("\\", "/").Trim('/');

            // Prepare regex if provided (treat empty as match-all)
            Regex? regex = null;
            if (!string.IsNullOrWhiteSpace(options.Pattern))
            {
                if (options.UseWildcards)
                {
                    string regexPattern = "^" + Regex.Escape(options.Pattern)
                        .Replace(@"\*", ".*")
                        .Replace(@"\?", ".") + "$";
                    regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                }
                else
                {
                    regex = new Regex(options.Pattern, RegexOptions.IgnoreCase);
                }
            }

            var files = new List<string>();
            EnumerateFiles(fileStore, baseDir, options.SearchRecursively, regex, files, cancellationToken);

            return new Result { Success = true, Files = files.ToArray(), };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ErrorHandler.Handle(e, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
        finally
        {
            fileStore?.Disconnect();

            NTStatus status = NTStatus.STATUS_NOT_IMPLEMENTED;
            try
            {
                client.ListShares(out status);
            }
            catch
            {
                // ignored
            }

            if (status == NTStatus.STATUS_SUCCESS) client.Logoff();
            client.Disconnect();
        }
    }

    private static void EnumerateFiles(
        ISMBFileStore fileStore,
        string currentDir,
        bool recursive,
        Regex? regex,
        List<string> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Open directory
        var status = fileStore.CreateFile(
            out var dirHandle,
            out var fileStatus,
            currentDir,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new Exception($"Failed to open directory '{currentDir}': {status}, file status: {fileStatus}");

        try
        {
            // Query entries in the directory
            status = fileStore.QueryDirectory(
                out var entries,
                dirHandle,
                "*",
                FileInformationClass.FileDirectoryInformation);

            if (status != NTStatus.STATUS_NO_MORE_FILES)
                throw new Exception($"Failed to query directory '{currentDir}': {status}");

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var e = (FileDirectoryInformation)entry;
                string name = e.FileName;
                if (name is "." or "..") continue;

                FileAttributes attrs = e.FileAttributes;
                bool isDirectory = (attrs & FileAttributes.Directory) == FileAttributes.Directory;

                var relativePath = string.IsNullOrEmpty(currentDir) ? name : $"{currentDir}/{name}";

                if (isDirectory)
                {
                    if (recursive)
                    {
                        EnumerateFiles(fileStore, relativePath, true, regex, results, cancellationToken);
                    }
                }
                else
                {
                    if (regex == null || regex.IsMatch(name) || regex.IsMatch(relativePath))
                    {
                        results.Add(relativePath);
                    }
                }
            }
        }
        finally
        {
            var closeStatus = fileStore.CloseFile(dirHandle);
            if (closeStatus != NTStatus.STATUS_SUCCESS)
            {
                // ignored
            }
        }
    }

    private static Tuple<string, string> GetDomainAndUsername(string username)
    {
        var domainAndUserName = username.Split('\\');
        return domainAndUserName.Length != 2
            ? throw new ArgumentException($@"UserName field must be of format domain\username was: {username}")
            : new Tuple<string, string>(domainAndUserName[0], domainAndUserName[1]);
    }
}
