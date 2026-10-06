namespace CivicLens.Core.Collection;

public sealed class NotModifiedObservation : Observation
{
    public NotModifiedObservation(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt, ObservationResponse response)
        : base(attemptId, sourceId, requestedUrl, finalUrl, observedAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode != 304)
            throw new ArgumentException("Not-modified observations require a 304 response.", nameof(response));
        Response = response;
    }

    public override ObservationResponse Response { get; }

    protected override bool OutcomeEquals(Observation other) => other is NotModifiedObservation unchanged &&
        Response.Equals(unchanged.Response);

    protected override void AddOutcomeHash(ref HashCode hash) => hash.Add(Response);
}
