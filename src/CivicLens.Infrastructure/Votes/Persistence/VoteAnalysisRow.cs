using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisRow : IAnalysisRow
{
    public string CandidateId { get; set; } = string.Empty;
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
    public int Priority { get; set; }

    string IAnalysisRow.SubjectId => CandidateId;
}
