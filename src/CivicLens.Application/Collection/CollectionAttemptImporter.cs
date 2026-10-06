using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

public static class CollectionAttemptImporter
{
    public static Task<CollectionImportDecision> ImportAsync(string attemptId, CollectionRequest request,
        CollectionResult result, ICollectionAttemptStore store, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.ImportAtomicallyAsync(CreateImport(attemptId, request, result), cancellationToken);
    }

    public static CollectionAttemptImport CreateImport(string attemptId, CollectionRequest request, CollectionResult result)
    {
        result.ValidateAgainst(request);
        var sentValidators = result.SentValidators is null ? null :
            new SentValidators(result.SentValidators.ETag, result.SentValidators.LastModified);
        return new CollectionAttemptImport(Map(attemptId, result), sentValidators);
    }

    private static CollectionAttemptResult Map(string attemptId, CollectionResult result)
    {
        var response = result.Response is null ? null : new CollectionResponse(result.Response.StatusCode,
            result.Response.ETag, result.Response.LastModified, result.Response.ContentType, result.Response.ContentEncodings);
        return result.Outcome switch
        {
            CollectionOutcome.Captured => new CapturedAttemptResult(attemptId, result.SourceId, result.RequestedUrl,
                result.FinalUrl, result.ObservedAt, response!, new CaptureIdentity(result.Capture!.Sha256, result.Capture.ByteLength)),
            CollectionOutcome.NotModified => new NotModifiedAttemptResult(attemptId, result.SourceId,
                result.RequestedUrl, result.FinalUrl, result.ObservedAt, response!),
            CollectionOutcome.Failed => new FailedAttemptResult(attemptId, result.SourceId, result.RequestedUrl,
                result.FinalUrl, result.ObservedAt, result.FailureCode!.Value.ToString(), response),
            CollectionOutcome.Deferred => new DeferredAttemptResult(attemptId, result.SourceId, result.RequestedUrl,
                result.FinalUrl, result.ObservedAt, result.FailureCode!.Value.ToString(), MapRetryDelay(result.RetryAfterSeconds), response),
            _ => throw new InvalidDataException("Collection result outcome is invalid.")
        };
    }

    private static TimeSpan? MapRetryDelay(long? seconds)
    {
        if (seconds is null) return null;
        var maximum = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond;
        if (seconds < 0 || seconds > maximum) throw new InvalidDataException("Collection retry delay is outside the supported range.");
        return TimeSpan.FromTicks(seconds.Value * TimeSpan.TicksPerSecond);
    }
}
