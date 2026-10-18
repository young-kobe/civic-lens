using CivicLens.Application.Analysis;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal interface IAnalysisRow
{
    string SubjectId { get; }
    string Task { get; }
    AnalysisState Status { get; set; }
    string? LeaseToken { get; set; }
    long? LeaseExpiresAt { get; set; }
    long Fence { get; set; }
    int Attempts { get; set; }
    long? RetryAt { get; set; }
    long ReservedTokens { get; set; }
    DateOnly? BudgetDay { get; set; }
    long? ReservedAtUtcTicks { get; set; }
    string? PendingRunId { get; set; }
    string? PendingInputHash { get; set; }
    string? RetryOfRunId { get; set; }
    string? ErrorCode { get; set; }
    long CreatedAtUtcTicks { get; }
    int Priority { get; set; }
}
