using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class ListPublishable(IDocumentChangeReviewStore documentChanges, IStatementReviewStore statements)
{
    public async Task<ReadyToPublishList> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var limit = PublishRelease.MaximumRecords;
        var changeCandidates = await documentChanges.ListUnpublishedApprovedDraftsAsync(limit + 1, cancellationToken);
        var statementCandidates = await statements.ListUnpublishedApprovedAsync(limit + 1, cancellationToken);
        var ready = ReadyChanges(changeCandidates).Concat(ReadyStatements(statementCandidates)).ToList();
        var hasMore = changeCandidates.Count > limit || statementCandidates.Count > limit || ready.Count > limit;
        return new([.. ready.Take(limit)], hasMore);
    }

    private static IEnumerable<ReadyToPublishRecord> ReadyChanges(IReadOnlyList<UnpublishedApprovedDraft> candidates) =>
        candidates
            .Where(candidate => PublishedRecordBuilder.IsPublishable(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount))
            .Select(candidate => new ReadyToPublishRecord(PublishedRecordKind.DocumentChange, candidate.Draft.DraftId,
                candidate.Draft.CurrentRevision.Headline, candidate.Draft.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber));

    private static IEnumerable<ReadyToPublishRecord> ReadyStatements(IReadOnlyList<UnpublishedApprovedStatement> candidates) =>
        candidates
            .Where(candidate => candidate.Statement.CurrentRevision is not null &&
                PublishedRecordBuilder.IsPublishable(candidate.Statement.CurrentStatus, candidate.Statement.UnresolvedConcernCount))
            .Select(candidate => new ReadyToPublishRecord(PublishedRecordKind.Statement, candidate.Statement.CandidateId,
                candidate.Statement.CurrentRevision!.Headline, candidate.Statement.CurrentRevision.RevisionNumber,
                candidate.PublishedRevisionNumber));
}
