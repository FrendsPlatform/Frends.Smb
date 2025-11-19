using System;
using Frends.Smb.DeleteDirectory.Definitions;

namespace Frends.Smb.DeleteDirectory.Helpers;

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
}
