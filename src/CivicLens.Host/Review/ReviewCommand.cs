using CivicLens.Application.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Review;

internal enum ReviewCommandFailure { None, AccessDenied, Conflict, Invalid, Unavailable }

internal static class ReviewCommand
{
    public const string AccessDenied = "/access-denied";

    public static async Task<(T? Result, ReviewCommandFailure Failure)> RunAsync<T>(Func<Task<T>> command, HttpResponse response,
        ILogger logger, CancellationToken requestAborted)
    {
        try
        {
            return (await command(), ReviewCommandFailure.None);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return (default, ReviewCommandFailure.AccessDenied); }
        catch (ReviewConflictException) { return Fail<T>(response, StatusCodes.Status409Conflict, ReviewCommandFailure.Conflict); }
        catch (ArgumentException) { return Fail<T>(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.Invalid); }
        catch (Exception exception)
        {
            logger.LogError(exception, "A review command failed.");
            return Fail<T>(response, StatusCodes.Status503ServiceUnavailable, ReviewCommandFailure.Unavailable);
        }
    }

    private static (T? Result, ReviewCommandFailure Failure) Fail<T>(HttpResponse response, int statusCode, ReviewCommandFailure failure)
    {
        response.StatusCode = statusCode;
        return (default, failure);
    }
}
