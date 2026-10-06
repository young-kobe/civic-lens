namespace CivicLens.Core.Collection;

public sealed class CapturedAttemptResult : CollectionAttemptResult
{
    public CapturedAttemptResult(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, CollectionResponse response, CaptureIdentity capture)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(capture);
        if (response.StatusCode != 200)
            throw new ArgumentException("Captured results require a successful response.", nameof(response));
        Response = response;
        Capture = capture;
    }

    public override CollectionResponse Response { get; }
    public CaptureIdentity Capture { get; }

    protected override bool OutcomeEquals(CollectionAttemptResult other) => other is CapturedAttemptResult captured &&
        Response.Equals(captured.Response) && Capture.Equals(captured.Capture);

    protected override void AddOutcomeHash(ref HashCode hash)
    {
        hash.Add(Response);
        hash.Add(Capture);
    }
}
