using CivicLens.Application.Analysis;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    BriefRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static BriefDraftCheckpoint From(AnalysisClaim claim, BriefRevision revision) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded,
            Revision: revision);
}
