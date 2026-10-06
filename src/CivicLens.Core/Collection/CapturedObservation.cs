namespace CivicLens.Core.Collection;

public sealed class CapturedObservation : Observation
{
    public CapturedObservation(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, ObservationResponse response, CaptureIdentity capture)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(capture);
        if (response.StatusCode != 200)
            throw new ArgumentException("Captured observations require a successful response.", nameof(response));
        Response = response;
        Capture = capture;
    }

    public override ObservationResponse Response { get; }
    public CaptureIdentity Capture { get; }

    protected override bool OutcomeEquals(Observation other) => other is CapturedObservation captured &&
        Response.Equals(captured.Response) && Capture.Equals(captured.Capture);

    protected override void AddOutcomeHash(ref HashCode hash)
    {
        hash.Add(Response);
        hash.Add(Capture);
    }
}
