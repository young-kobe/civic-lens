namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementDraftRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
