using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.CopyFiles.Definitions;
using NUnit.Framework;
using SMBLibrary;
using SMBLibrary.Client;

namespace Frends.Smb.CopyFiles.Tests;

[TestFixture]
public class CopyFilesTests : SmbTestBase
{
    [TestCase("src/test1.txt", PatternMatchingMode.Regex, "", 1)]
    [TestCase("src", PatternMatchingMode.Regex, "^test.*\\.txt", 4)]
    [TestCase("src", PatternMatchingMode.Wildcards, "*.txt", 5)]
    [TestCase("src", PatternMatchingMode.Wildcards, "test?.*", 6)]
    public void CopySpecificFileUsingPattern(
        string sourcePath,
        PatternMatchingMode patternMatchingMode,
        string pattern,
        int expectedCopiesCount)
    {
        Input.SourcePath = sourcePath;
        Input.TargetPath = "dst/src";
        Options.PatternMatchingMode = patternMatchingMode;
        Options.Pattern = pattern;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Files.Count, Is.EqualTo(expectedCopiesCount));
        var resultDir = File.Exists(Path.Combine(TestDirPath, sourcePath))
            ? Path.Combine(TestDirPath, "dst", "src")
            : Path.Combine(TestDirPath, "dst", "src", "src");
        var filesCount = Directory.GetFiles(resultDir).Length;
        Assert.That(filesCount, Is.EqualTo(expectedCopiesCount));
    }

    [Test]
    public void CreateTargetDirectoryIsTrue()
    {
        Options.CreateTargetDirectories = true;
        Input.SourcePath = "src/subDir";
        Options.Recursive = false;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
    }

    [Test]
    public void CreateTargetDirectoryIsFalse()
    {
        Options.CreateTargetDirectories = false;
        Input.SourcePath = "src/subDir";
        Options.Recursive = false;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(
            result.Error.Message,
            Contains.Substring("Target directory does not exist and Options.CreateTargetDirectories is disabled."));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.False);
    }

    [Test]
    public void FileExistsActionIsThrow()
    {
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        PathString expectedMessage = @"File dst\old.foo already exists.";
        PathString actualMessage = result.Error.Message;
        Assert.That(actualMessage.Value, Contains.Substring(expectedMessage));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
    }

    [Test]
    public async Task RollbackTmpFilesWhenErrorOccurred()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        PathString path = "src/error";
        Input.SourcePath = path.Value;
        Options.Recursive = true;
        Options.CreateTargetDirectories = false;

        TestContext.WriteLine("Before CopyFiles");
        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        TestContext.WriteLine($"After CopyFiles - Success: {result.Success}, Error: {result.Error?.Message}");

        Assert.That(result.Success, Is.False);

        // Wait a bit for operations to complete
        await Task.Delay(500);

        // Check what files exist through SMB
        TestContext.WriteLine("Checking file existence through SMB...");
        var originalExists = FileExistsThroughSmb("dst/error/old.foo");
        TestContext.WriteLine($"dst/error/old.foo exists: {originalExists}");

        // Check for temp files that might not have been rolled back
        var tempFilePattern = await ListFilesThroughSmbAsync("dst/error");
        TestContext.WriteLine($"Files in dst/error: {string.Join(", ", tempFilePattern)}");

        // Verify through SMB instead of local filesystem to avoid sync issues
        var content = ReadFileThroughSmb("dst/error/old.foo");
        TestContext.WriteLine($"Content of dst/error/old.foo: '{content}' (length: {content.Length})");

        Assert.That(content, Is.EqualTo("old test content"));
    }

    [Test]
    public void RemoveTmpFilesWhenFinishOverwriting()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        PathString path = "src/error";
        Input.SourcePath = path.Value;
        Options.CreateTargetDirectories = false;
        Options.Recursive = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "error", "old.foo")), Is.True);
        Assert.That(Directory.GetFiles(Path.Combine(TestDirPath, "dst", "error")).Length, Is.EqualTo(1));
    }

    [Test]
    public async Task RemoveNewFilesWhenErrorOccured()
    {
        Options.IfTargetFileExists = FileExistsAction.Rename;
        Input.SourcePath = "src/error";
        Options.Recursive = true;
        Options.CreateTargetDirectories = false;

        TestContext.WriteLine("Before CopyFiles");
        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);
        TestContext.WriteLine($"After CopyFiles - Success: {result.Success}, Error: {result.Error?.Message}");

        Assert.That(result.Success, Is.False);

        // Wait a bit for operations to complete
        await Task.Delay(500);

        // Check what files exist
        TestContext.WriteLine("Checking file existence through SMB...");
        var files = await ListFilesThroughSmbAsync("dst/error");
        TestContext.WriteLine($"Files in dst/error: {string.Join(", ", files)}");

        // Verify through SMB instead of local filesystem
        var originalFileExists = FileExistsThroughSmb("dst/error/old.foo");
        TestContext.WriteLine($"dst/error/old.foo exists: {originalFileExists}");
        Assert.That(originalFileExists, Is.True, "Original file should exist");

        var renamedFileExists = FileExistsThroughSmb("dst/error/old(1).foo");
        TestContext.WriteLine($"dst/error/old(1).foo exists: {renamedFileExists}");
        Assert.That(renamedFileExists, Is.False, "Renamed file should be cleaned up");
    }

    [Test]
    public void FileExistsActionIsOverwrite()
    {
        Options.IfTargetFileExists = FileExistsAction.Overwrite;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old.foo")), Is.EqualTo("new test content"));
    }

    [Test]
    public void FileExistsActionIsRename()
    {
        Options.IfTargetFileExists = FileExistsAction.Rename;
        Input.SourcePath = "src/old.foo";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old.foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old.foo")), Is.EqualTo("old test content"));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "old(1).foo")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(TestDirPath, "dst", "old(1).foo")), Is.EqualTo("new test content"));
    }

    [Test]
    public void PreserveDirectoryStructureIsTrue()
    {
        Options.PreserveDirectoryStructure = true;
        Options.Recursive = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.True);
    }

    [Test]
    public void PreserveDirectoryStructureIsFalse()
    {
        Options.PreserveDirectoryStructure = false;
        Options.Recursive = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        PathString expectedMessage = @"File dst\sub.foo already exists.";
        Assert.That(result.Error.Message, Contains.Substring(expectedMessage));
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.False);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }

    [Test]
    public void CopyRecursiveIsTrue()
    {
        Options.Recursive = true;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.True);
    }

    [Test]
    public void CopyRecursiveIsFalse()
    {
        Options.Recursive = false;
        Input.SourcePath = "src/subDir";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "sub.foo")), Is.True);
        Assert.That(File.Exists(Path.Combine(TestDirPath, "dst", "subDir", "subSubDir", "sub.foo")), Is.False);
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_PartialSuccess_ReturnsSuccessWithFailures()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = true;
        Options.ThrowErrorOnFailure = false;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PreserveDirectoryStructure = false;
        Options.Recursive = false;

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.FileFailures, Is.Not.Empty);
        Assert.That(result.Error.FileFailures.Any(f => f.SourcePath.Contains("old.foo")), Is.True);
        Assert.That(result.Error.AdditionalInfo, Is.InstanceOf<AggregateException>());
        Assert.That(result.Files.Count, Is.GreaterThan(0));
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_AllSucceed_ErrorIsNull()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = true;
        Options.ThrowErrorOnFailure = false;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PatternMatchingMode = PatternMatchingMode.Wildcards;
        Options.Pattern = "*.txt";

        var result = Smb.CopyFiles(Input, Connection, Options, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Files.Count, Is.GreaterThan(0));
    }

    [Test]
    public void CopyFiles_ContinueOnFailure_False_ThrowsOnFirstFailure()
    {
        Input.SourcePath = "src";
        Input.TargetPath = "dst";
        Options.ContinueOnFailure = false;
        Options.ThrowErrorOnFailure = true;
        Options.IfTargetFileExists = FileExistsAction.Throw;
        Options.PreserveDirectoryStructure = false;

        Assert.Throws<Exception>(() =>
            Smb.CopyFiles(Input, Connection, Options, CancellationToken.None));
    }

    private static (string domain, string username) GetDomainAndUsername(string usernameWithDomain)
    {
        var parts = usernameWithDomain.Split('\\');
        if (parts.Length == 2)
            return (parts[0], parts[1]);
        return (string.Empty, usernameWithDomain);
    }

    private string ReadFileThroughSmb(string path)
    {
        SMB2Client client = null;
        ISMBFileStore fileStore = null;

        try
        {
            client = new SMB2Client();
            var (domain, username) = GetDomainAndUsername(Connection.Username);

            if (!System.Net.IPAddress.TryParse(Connection.Server, out var serverAddress))
                throw new Exception($"Could not parse server address: {Connection.Server}");

            if (!client.Connect(serverAddress, SMBTransportType.DirectTCPTransport))
                throw new Exception("Failed to connect to SMB server");

            var status = client.Login(domain, username, Connection.Password);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new Exception($"SMB login failed: {status}");

            fileStore = client.TreeConnect(Connection.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to connect to share: {status}");

            // Open file
            status = fileStore.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
                throw new Exception($"Failed to open file: {status}");

            try
            {
                // Read file content
                var content = string.Empty;
                long bytesRead = 0;

                while (true)
                {
                    status = fileStore.ReadFile(out var data, handle, bytesRead, 64 * 1024);

                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                        throw new Exception($"Failed to read file: {status}");

                    if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                        break;

                    content += System.Text.Encoding.UTF8.GetString(data);
                    bytesRead += data.Length;
                }

                return content;
            }
            finally
            {
                fileStore.CloseFile(handle);
            }
        }
        finally
        {
            fileStore?.Disconnect();

            if (client != null)
            {
                try
                {
                    client.ListShares(out var logoffStatus);
                    if (logoffStatus == NTStatus.STATUS_SUCCESS)
                        client.Logoff();
                }
                catch
                {
                    // Ignore
                }

                client.Disconnect();
            }
        }
    }

    private bool FileExistsThroughSmb(string path)
    {
        SMB2Client client = null;
        ISMBFileStore fileStore = null;

        try
        {
            client = new SMB2Client();
            var (domain, username) = GetDomainAndUsername(Connection.Username);

            if (!System.Net.IPAddress.TryParse(Connection.Server, out var serverAddress))
                return false;

            if (!client.Connect(serverAddress, SMBTransportType.DirectTCPTransport))
                return false;

            var status = client.Login(domain, username, Connection.Password);
            if (status != NTStatus.STATUS_SUCCESS)
                return false;

            fileStore = client.TreeConnect(Connection.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS)
                return false;

            // Try to open file
            status = fileStore.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                fileStore.CloseFile(handle);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            fileStore?.Disconnect();

            if (client != null)
            {
                try
                {
                    client.ListShares(out var logoffStatus);
                    if (logoffStatus == NTStatus.STATUS_SUCCESS)
                        client.Logoff();
                }
                catch
                {
                    // Ignore
                }

                client.Disconnect();
            }
        }
    }

    private Task<List<string>> ListFilesThroughSmbAsync(string path)
    {
        var files = new List<string>();
        SMB2Client client = null;
        ISMBFileStore fileStore = null;

        try
        {
            client = new SMB2Client();
            var (domain, username) = GetDomainAndUsername(Connection.Username);

            if (!System.Net.IPAddress.TryParse(Connection.Server, out var serverAddress))
                return Task.FromResult(files);

            if (!client.Connect(serverAddress, SMBTransportType.DirectTCPTransport))
                return Task.FromResult(files);

            var status = client.Login(domain, username, Connection.Password);
            if (status != NTStatus.STATUS_SUCCESS)
                return Task.FromResult(files);

            fileStore = client.TreeConnect(Connection.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS)
                return Task.FromResult(files);

            // Open directory
            status = fileStore.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_READ,
                SMBLibrary.FileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
                return Task.FromResult(files);

            try
            {
                status = fileStore.QueryDirectory(
                    out List<QueryDirectoryFileInformation> fileList,
                    handle,
                    "*",
                    FileInformationClass.FileDirectoryInformation);

                if (status == NTStatus.STATUS_SUCCESS || status == NTStatus.STATUS_NO_MORE_FILES)
                {
                    foreach (var file in fileList.Cast<FileDirectoryInformation>())
                    {
                        if (file.FileName != "." && file.FileName != "..")
                            files.Add(file.FileName);
                    }
                }
            }
            finally
            {
                fileStore.CloseFile(handle);
            }

            return Task.FromResult(files);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Error listing files: {ex.Message}");
            return Task.FromResult(files);
        }
        finally
        {
            fileStore?.Disconnect();

            if (client != null)
            {
                try
                {
                    client.ListShares(out var logoffStatus);
                    if (logoffStatus == NTStatus.STATUS_SUCCESS)
                        client.Logoff();
                }
                catch
                {
                    // Ignore
                }

                client.Disconnect();
            }
        }
    }
}