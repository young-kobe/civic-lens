namespace CivicLens.Core.Collection;

public sealed class NotModifiedAttemptResult : CollectionAttemptResult
{
    public NotModifiedAttemptResult(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, CollectionResponse response)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode != 304)
            throw new ArgumentException("Not-modified results require a 304 response.", nameof(response));
        Response = response;
    }

    public override CollectionResponse Response { get; }

    protected override bool OutcomeEquals(CollectionAttemptResult other) => other is NotModifiedAttemptResult unchanged &&
        Response.Equals(unchanged.Response);

    protected override void AddOutcomeHash(ref HashCode hash) => hash.Add(Response);
}
