using CivicLens.Application.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisRow : IAnalysisRow
{
    public string PairId { get; set; } = string.Empty;
    public string StatementCandidateId { get; set; } = string.Empty;
    public RelationshipRecordKind TargetKind { get; set; }
    public string TargetRecordId { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public AnalysisState Status { get; set; }
    public string? LeaseToken { get; set; }
    public long? LeaseExpiresAt { get; set; }
    public long Fence { get; set; }
    public int Attempts { get; set; }
    public long? RetryAt { get; set; }
    public long ReservedTokens { get; set; }
    public DateOnly? BudgetDay { get; set; }
    public long? ReservedAtUtcTicks { get; set; }
    public string? PendingRunId { get; set; }
    public string? PendingInputHash { get; set; }
    public string? RetryOfRunId { get; set; }
    public string? ErrorCode { get; set; }
    public long CreatedAtUtcTicks { get; set; }

    string IAnalysisRow.SubjectId => PairId;
}
