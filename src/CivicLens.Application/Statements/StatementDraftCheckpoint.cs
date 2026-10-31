using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    StatementDraftRevision? Revision = null, ApprovalCheck? CodeApproval = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static StatementDraftCheckpoint From(AnalysisClaim claim, StatementDraftRevision revision, ApprovalCheck codeApproval) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded,
            Revision: revision, CodeApproval: codeApproval);
}
