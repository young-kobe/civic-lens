namespace CivicLens.Core.Collection;

/// <summary>Shared identity and time for one immutable source attempt.</summary>
public abstract class Observation : IEquatable<Observation>
{
    private protected Observation(string attemptId, string sourceId, string requestedUrl, string finalUrl,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("Attempt ID is required.", nameof(attemptId));
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Source ID is required.", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(requestedUrl)) throw new ArgumentException("Requested URL is required.", nameof(requestedUrl));
        if (string.IsNullOrWhiteSpace(finalUrl)) throw new ArgumentException("Final URL is required.", nameof(finalUrl));
        if (observedAt == default) throw new ArgumentException("Observed time is required.", nameof(observedAt));

        AttemptId = attemptId;
        SourceId = sourceId;
        RequestedUrl = requestedUrl;
        FinalUrl = finalUrl;
        ObservedAt = observedAt;
    }

    public string AttemptId { get; }
    public string SourceId { get; }
    public string RequestedUrl { get; }
    public string FinalUrl { get; }
    public DateTimeOffset ObservedAt { get; }
    public abstract ObservationResponse? Response { get; }

    protected abstract bool OutcomeEquals(Observation other);
    protected abstract void AddOutcomeHash(ref HashCode hash);

    public bool Equals(Observation? other) => other is not null && GetType() == other.GetType() &&
        AttemptId == other.AttemptId && SourceId == other.SourceId && RequestedUrl == other.RequestedUrl &&
        FinalUrl == other.FinalUrl && ObservedAt == other.ObservedAt && OutcomeEquals(other);

    public override bool Equals(object? obj) => obj is Observation other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());
        hash.Add(AttemptId);
        hash.Add(SourceId);
        hash.Add(RequestedUrl);
        hash.Add(FinalUrl);
        hash.Add(ObservedAt);
        AddOutcomeHash(ref hash);
        return hash.ToHashCode();
    }
}
