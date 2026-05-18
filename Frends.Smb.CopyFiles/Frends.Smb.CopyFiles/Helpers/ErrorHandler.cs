using System;
using System.Collections.Generic;
using System.Linq;
using Frends.Smb.CopyFiles.Definitions;

namespace Frends.Smb.CopyFiles.Helpers;

internal static class ErrorHandler
{
    internal static Result Handle(Exception exception, bool throwOnFailure, string errorMessageOnFailure)
    {
        if (throwOnFailure)
        {
            if (string.IsNullOrEmpty(errorMessageOnFailure))
                throw new Exception(exception.Message, exception);

            throw new Exception(errorMessageOnFailure, exception);
        }

        var errorMessage = !string.IsNullOrEmpty(errorMessageOnFailure)
            ? $"{errorMessageOnFailure}: {exception.Message}"
            : exception.Message;

        var error = new Error { Message = errorMessage, AdditionalInfo = exception, };

        return new Result { Success = false, Error = error };
    }

    internal static Result HandlePartialSuccess(
    List<FileFailure> fileFailures,
    List<FileItem> movedFiles,
    int totalFiles)
    {
        var aggregated = new AggregateException(
            fileFailures.Select(f => f.AdditionalInfo));

        return new Result
        {
            Success = true,
            Files = movedFiles,
            Error = new Error
            {
                Message = $"{fileFailures.Count} of {totalFiles} file(s) failed to move. {movedFiles.Count} succeeded.",
                AdditionalInfo = aggregated,
                FileFailures = fileFailures,
            },
        };
    }
}
