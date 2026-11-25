using CivicLens.Application.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public interface IStatementCodeApprovalRecheckStore
{
    Task<IReadOnlyList<StatementCodeApprovalCandidate>> ListAwaitingAsync(int limit, CancellationToken cancellationToken);

    Task<StatementCodeApprovalOutcome> ApplyAsync(string candidateId, int expectedRevisionNumber, ApprovalCheck check,
        CancellationToken cancellationToken);
}

public sealed record StatementCodeApprovalCandidate(string CandidateId, StatementDraftEvidence Evidence, StatementDraftRevision CurrentRevision);

public enum StatementCodeApprovalOutcome { Approved, StillHeld, Skipped }
