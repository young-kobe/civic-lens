using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Tests.Application.Statements;

internal sealed class FakeStatementReviewStore : IStatementReviewStore
{
    public StatementDraftRevision? Created { get; private set; }
    public StatementDraftRevision? Saved { get; private set; }
    public bool? SavedDateChecked { get; private set; }
    public ReviewDecision? Decided { get; private set; }
    public bool DecideCalled { get; private set; }
    public StatementAllowedSelections? ApprovalSelections { get; private set; }
    public string? ListedCursor { get; private set; }

    public Task<StatementDraftRevision> CreateAsync(string actorSubject, StatementDraftRevision revision, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        Created = revision;
        return Task.FromResult(revision);
    }

    public Task<StatementDraftRevision> SaveRevisionAsync(string actorSubject, StatementDraftRevision revision, int expectedRevisionNumber,
        bool statementDateChecked, string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        Saved = revision;
        SavedDateChecked = statementDateChecked;
        return Task.FromResult(revision);
    }

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, StatementAllowedSelections? approvalSelections, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken)
    {
        DecideCalled = true;
        Decided = decision;
        ApprovalSelections = approvalSelections;
        return Task.FromResult(decision);
    }

    public Task<StatementReviewDetail?> GetAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<StatementReviewDetail?>(null);

    public Task<StatementReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken)
    {
        ListedCursor = cursor?.Encode();
        return Task.FromResult(new StatementReviewPage([], null, null));
    }

    public Task<StatementNotInReviewPage> ListNotInReviewAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        ListedCursor = cursor?.Encode();
        return Task.FromResult(new StatementNotInReviewPage([], null, null));
    }
}
