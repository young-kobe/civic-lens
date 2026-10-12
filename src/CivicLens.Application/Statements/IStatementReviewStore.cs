using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public interface IStatementReviewStore
{
    Task<StatementDraftRevision> CreateAsync(string actorSubject, StatementDraftRevision revision, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);

    Task<StatementDraftRevision> SaveRevisionAsync(string actorSubject, StatementDraftRevision revision, int expectedRevisionNumber,
        bool statementDateChecked, string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, StatementAllowedSelections? approvalSelections, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<StatementReviewDetail?> GetAsync(string candidateId, CancellationToken cancellationToken);

    Task<StatementReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken);

    Task<StatementNotInReviewPage> ListNotInReviewAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken);
}
