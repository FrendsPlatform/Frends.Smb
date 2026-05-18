using System;
using System.Collections.Generic;
using System.Linq;
using Frends.Smb.DeleteFiles.Definitions;

namespace Frends.Smb.DeleteFiles.Helpers;

internal static class ErrorHandler
{
    internal static Result Handle(Exception exception, bool throwOnFailure, string errorMessageOnFailure)
    {
        if (throwOnFailure)
        {
            if (!string.IsNullOrEmpty(errorMessageOnFailure))
            {
                throw new Exception(errorMessageOnFailure, exception);
            }

            throw exception;
        }

        var errorMessage = string.IsNullOrEmpty(errorMessageOnFailure)
            ? exception.Message
            : $"{errorMessageOnFailure}: {exception.Message}";

        var error = new Error
        {
            Message = errorMessage,
            AdditionalInfo = exception,
        };

        return new Result
        {
            Success = false,
            Error = error,
            FilesDeleted = new List<FileItem>(),
            TotalFilesDeleted = 0,
        };
    }

    internal static Result HandlePartialSuccess(
    List<FileFailure> fileFailures,
    List<FileItem> deletedFiles,
    int totalFiles)
    {
        return new Result
        {
            Success = true,
            FilesDeleted = deletedFiles,
            TotalFilesDeleted = deletedFiles.Count,
            Error = new Error
            {
                Message = $"{fileFailures.Count} of {totalFiles} file(s) failed to delete. {deletedFiles.Count} succeeded.",
                AdditionalInfo = new AggregateException(fileFailures.Select(f => f.AdditionalInfo)),
                FileFailures = fileFailures,
            },
        };
    }
}
