namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementCandidateRow
{
    public string CandidateId { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string ExtractionId { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
}
