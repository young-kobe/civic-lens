using CivicLens.Application.Analysis;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    VoteDraftRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static VoteDraftCheckpoint From(AnalysisClaim claim, VoteDraftRevision revision) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded,
            Revision: revision);
}
