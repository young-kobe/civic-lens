using System.Collections.Immutable;
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

    public StatementReviewRead? Read { get; set; }

    public Task<StatementReviewRead?> GetAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult(Read);

    public Task<StatementTriageRead> ListTriageAsync(PageCursor? cursor, DraftListFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<StatementReviewCounts> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<StatementReviewPage> ListAsync(PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken)
    {
        ListedCursor = cursor?.Encode();
        return Task.FromResult(new StatementReviewPage([], null, null));
    }

    public Task<StatementNotInReviewPage> ListNotInReviewAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        ListedCursor = cursor?.Encode();
        return Task.FromResult(new StatementNotInReviewPage([], null, null));
    }

    public Dictionary<string, PublishableStatement> Publishable { get; } = [];
    public List<UnpublishedApprovedStatement> Unpublished { get; } = [];
    public int PublicationReadCount { get; private set; }
    public int UnpublishedLimit { get; private set; }
    public int UnpublishedReadCount { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedStatement>> ListUnpublishedApprovedAsync(int limit, CancellationToken cancellationToken)
    {
        UnpublishedReadCount++;
        UnpublishedLimit = limit;
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedStatement>>([.. Unpublished.Take(limit)]);
    }

    public Task<IReadOnlyDictionary<string, PublishableStatement>> GetForPublicationAsync(ImmutableArray<string> candidateIds,
        CancellationToken cancellationToken)
    {
        PublicationReadCount++;
        IReadOnlyDictionary<string, PublishableStatement> found = candidateIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => Publishable[id]);
        return Task.FromResult(found);
    }
}
