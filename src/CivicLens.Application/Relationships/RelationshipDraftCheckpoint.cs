using CivicLens.Application.Analysis;
using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    RelationshipRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static RelationshipDraftCheckpoint From(AnalysisClaim claim, RelationshipRevision revision) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded,
            Revision: revision);
}
