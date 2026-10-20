namespace CivicLens.PublicApi.Ingest;

public enum IngestOutcome { NotModified, NotNewer, Ingested }

public sealed record RecordFailure(string RecordId, string Reason);

public sealed record IngestResult(IngestOutcome Outcome, int ReleaseNumber, int Indexed, int Closed, IReadOnlyList<RecordFailure> Failures)
{
    public static IngestResult NotModified { get; } = new(IngestOutcome.NotModified, 0, 0, 0, []);
}
