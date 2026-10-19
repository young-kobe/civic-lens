using CivicLens.Application.Review;

namespace CivicLens.Host.Review;

internal enum ReviewCommandFailure { None, Conflict, Invalid, Unavailable }

internal static class ReviewCommand
{
    public const string AccessDenied = "/access-denied";

    public static async Task<(string? Target, ReviewCommandFailure Failure)> RunAsync(Func<Task<string>> command, HttpResponse response,
        CancellationToken requestAborted)
    {
        try
        {
            return (await command(), ReviewCommandFailure.None);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return (AccessDenied, ReviewCommandFailure.None); }
        catch (ReviewConflictException) { return Fail(response, StatusCodes.Status409Conflict, ReviewCommandFailure.Conflict); }
        catch (ArgumentException) { return Fail(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.Invalid); }
        catch (Exception) { return Fail(response, StatusCodes.Status503ServiceUnavailable, ReviewCommandFailure.Unavailable); }
    }

    private static (string? Target, ReviewCommandFailure Failure) Fail(HttpResponse response, int statusCode, ReviewCommandFailure failure)
    {
        response.StatusCode = statusCode;
        return (null, failure);
    }
}
