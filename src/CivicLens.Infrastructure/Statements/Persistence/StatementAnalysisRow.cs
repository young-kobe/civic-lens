namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisRow
{
    public string CandidateId { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
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
}
