using System;
using System.Threading;
using System.Threading.Tasks;
using Frends.Smb.RenameFile.Definitions;
using NUnit.Framework;

namespace Frends.Smb.RenameFile.Tests;

[TestFixture]
public class ErrorHandlerTest
{
    private const string CustomErrorMessage = "CustomErrorMessage";

    [Test]
    public void Should_Throw_Error_When_ThrowErrorOnFailure_Is_True()
    {
        var ex = Assert.Throws<System.Exception>(() =>
                 Smb.RenameFile(DefaultInput(), DefaultConnection(), DefaultOptions(), CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public void Should_Return_Failed_Result_When_ThrowErrorOnFailure_Is_FalseAsync()
    {
        var options = DefaultOptions();
        options.ThrowErrorOnFailure = false;
        var result = Smb.RenameFile(DefaultInput(), DefaultConnection(), options, CancellationToken.None);
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Should_Use_Custom_ErrorMessageOnFailure()
    {
        var options = DefaultOptions();
        options.ErrorMessageOnFailure = CustomErrorMessage;
        var ex = Assert.Throws<System.Exception>(() =>
            Smb.RenameFile(DefaultInput(), DefaultConnection(), options, CancellationToken.None));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Contains.Substring(CustomErrorMessage));
    }

    private static Input DefaultInput() => new()
    {
        Path = @"\nonexistent-file.txt",
    };

    private static Connection DefaultConnection() => new();

    private static Options DefaultOptions() => new()
    {
        ThrowErrorOnFailure = true,
        ErrorMessageOnFailure = string.Empty,
    };
}
