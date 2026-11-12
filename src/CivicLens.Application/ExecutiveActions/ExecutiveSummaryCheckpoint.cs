using CivicLens.Application.Analysis;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

public sealed record ExecutiveSummaryCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null, ExecutiveActionRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static ExecutiveSummaryCheckpoint From(AnalysisClaim claim, ExecutiveActionRevision revision) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded, Revision: revision);
}
