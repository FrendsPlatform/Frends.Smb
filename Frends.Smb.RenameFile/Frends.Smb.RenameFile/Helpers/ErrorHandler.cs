using System;
using Frends.Smb.RenameFile.Definitions;

namespace Frends.Smb.RenameFile.Helpers;

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

            throw new Exception(errorMessageOnFailure, exception);
        }

        var errorMessage = string.IsNullOrEmpty(errorMessageOnFailure)
            ? exception.Message
            : $"{errorMessageOnFailure}: {exception.Message}";

        var error = new Error
        {
            Message = errorMessage,
            AdditionalInfo = exception,
        };

        return new Result { Success = false, Error = error };
    }
}
