using CivicLens.Application.Review;
using CivicLens.Core.Review;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Review;

internal enum ReviewCommandFailure { None, AccessDenied, Conflict, NothingToSave, CycleFull, ApprovalStale, Invalid, Unavailable }

internal static class ReviewCommand
{
    public const string AccessDenied = "/access-denied";

    public static async Task<(T? Result, ReviewCommandFailure Failure, string? Detail)> RunAsync<T>(Func<Task<T>> command, HttpResponse response,
        ILogger logger, CancellationToken requestAborted)
    {
        try
        {
            return (await command(), ReviewCommandFailure.None, null);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return (default, ReviewCommandFailure.AccessDenied, null); }
        catch (NothingToSaveException) { return Fail<T>(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.NothingToSave); }
        catch (ReviewConflictException) { return Fail<T>(response, StatusCodes.Status409Conflict, ReviewCommandFailure.Conflict); }
        catch (StaleApprovalException stale) { return Fail<T>(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.ApprovalStale, stale.Message); }
        catch (ReviewCycleFullException) { return Fail<T>(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.CycleFull); }
        catch (ArgumentException) { return Fail<T>(response, StatusCodes.Status400BadRequest, ReviewCommandFailure.Invalid); }
        catch (Exception exception)
        {
            logger.LogError(exception, "A review command failed.");
            return Fail<T>(response, StatusCodes.Status503ServiceUnavailable, ReviewCommandFailure.Unavailable);
        }
    }

    private static (T? Result, ReviewCommandFailure Failure, string? Detail) Fail<T>(HttpResponse response, int statusCode,
        ReviewCommandFailure failure, string? detail = null)
    {
        response.StatusCode = statusCode;
        return (default, failure, detail);
    }
}
