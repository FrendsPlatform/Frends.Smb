using System;
using System.Collections.Generic;
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
}
