using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

/// <summary>Collects, validates, maps, and atomically imports one verified collection result.</summary>
/// <remarks>Each call performs a fresh collection. Replaying a saved receipt requires a separate verified-token import path.</remarks>
public sealed class CollectAndImportCollectionAttempt(ICollectorProcess collector, ICollectionAttemptStore store)
{
    public async Task<CollectionImportDecision> ExecuteAsync(string attemptId, CollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var result = await collector.RunAsync(request, cancellationToken);
        result.ValidateAgainst(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sentValidators = result.SentValidators is null
            ? null
            : new SentValidators(result.SentValidators.ETag, result.SentValidators.LastModified);
        var attempt = new CollectionAttemptImport(Map(attemptId, result), sentValidators);
        return await store.ImportAtomicallyAsync(attempt, cancellationToken);
    }

    private static CollectionAttemptResult Map(string attemptId, CollectionResult result)
    {
        var response = result.Response is null ? null : new CollectionResponse(
            result.Response.StatusCode,
            result.Response.ETag,
            result.Response.LastModified,
            result.Response.ContentType,
            result.Response.ContentEncodings);

        return result.Outcome switch
        {
            CollectionOutcome.Captured => new CapturedAttemptResult(attemptId, result.SourceId,
                result.RequestedUrl, result.FinalUrl, result.ObservedAt, response!,
                new CaptureIdentity(result.Capture!.Sha256, result.Capture.ByteLength)),
            CollectionOutcome.NotModified => new NotModifiedAttemptResult(attemptId, result.SourceId,
                result.RequestedUrl, result.FinalUrl, result.ObservedAt, response!),
            CollectionOutcome.Failed => new FailedAttemptResult(attemptId, result.SourceId,
                result.RequestedUrl, result.FinalUrl, result.ObservedAt, result.FailureCode!.Value.ToString(), response),
            CollectionOutcome.Deferred => new DeferredAttemptResult(attemptId, result.SourceId,
                result.RequestedUrl, result.FinalUrl, result.ObservedAt, result.FailureCode!.Value.ToString(),
                MapRetryDelay(result.RetryAfterSeconds), response),
            _ => throw new InvalidDataException("Collection result outcome is invalid.")
        };
    }

    private static TimeSpan? MapRetryDelay(long? retryAfterSeconds)
    {
        if (retryAfterSeconds is null)
            return null;

        var maximumSeconds = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond;
        var seconds = retryAfterSeconds.Value;
        if (seconds < 0 || seconds > maximumSeconds)
            throw new InvalidDataException("Collection retry delay is outside the supported range.");

        return TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond);
    }
}
