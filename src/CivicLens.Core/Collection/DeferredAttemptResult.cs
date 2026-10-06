namespace CivicLens.Core.Collection;

public sealed class DeferredAttemptResult : CollectionAttemptResult
{
    public DeferredAttemptResult(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, string failureCode, TimeSpan? retryDelay = null,
        CollectionResponse? response = null)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
            throw new ArgumentException("Failure information is required.", nameof(failureCode));
        if (retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        FailureCode = failureCode;
        RetryDelay = retryDelay;
        Response = response;
    }

    public string FailureCode { get; }
    public TimeSpan? RetryDelay { get; }
    public override CollectionResponse? Response { get; }

    protected override bool OutcomeEquals(CollectionAttemptResult other) => other is DeferredAttemptResult deferred &&
        FailureCode == deferred.FailureCode && RetryDelay == deferred.RetryDelay && Equals(Response, deferred.Response);

    protected override void AddOutcomeHash(ref HashCode hash)
    {
        hash.Add(FailureCode);
        hash.Add(RetryDelay);
        hash.Add(Response);
    }
}
