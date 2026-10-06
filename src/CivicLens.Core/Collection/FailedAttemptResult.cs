namespace CivicLens.Core.Collection;

public sealed class FailedAttemptResult : CollectionAttemptResult
{
    public FailedAttemptResult(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, string failureCode, CollectionResponse? response = null)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
            throw new ArgumentException("Failure information is required.", nameof(failureCode));
        FailureCode = failureCode;
        Response = response;
    }

    public string FailureCode { get; }
    public override CollectionResponse? Response { get; }

    protected override bool OutcomeEquals(CollectionAttemptResult other) => other is FailedAttemptResult failed &&
        FailureCode == failed.FailureCode && Equals(Response, failed.Response);

    protected override void AddOutcomeHash(ref HashCode hash)
    {
        hash.Add(FailureCode);
        hash.Add(Response);
    }
}
