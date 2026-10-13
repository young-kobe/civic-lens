using System.Collections.Immutable;
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
        var changeRead = documentChanges.ListUnpublishedApprovedDraftsAsync(limit + 1, cancellationToken);
        var statementRead = statements.ListUnpublishedApprovedAsync(limit + 1, cancellationToken);
        await Task.WhenAll(changeRead, statementRead);
        var changeCandidates = await changeRead;
        var statementCandidates = await statementRead;
        var readyChanges = ReadyChanges(changeCandidates);
        var readyStatements = ReadyStatements(statementCandidates);
        var hasMore = changeCandidates.Count > limit || statementCandidates.Count > limit
            || readyChanges.Count + readyStatements.Count > limit;
        return new(ShareLimitBetweenKinds(readyChanges, readyStatements, limit), hasMore);
    }

    private static ImmutableArray<ReadyToPublishRecord> ShareLimitBetweenKinds(IReadOnlyList<ReadyToPublishRecord> changes,
        IReadOnlyList<ReadyToPublishRecord> statements, int limit)
    {
        var changeCount = Math.Min(changes.Count, Math.Max(limit / 2, limit - statements.Count));
        var statementCount = Math.Min(statements.Count, limit - changeCount);
        return [.. changes.Take(changeCount), .. statements.Take(statementCount)];
    }

    private static IReadOnlyList<ReadyToPublishRecord> ReadyChanges(IReadOnlyList<UnpublishedApprovedDraft> candidates) =>
        candidates
            .Where(candidate => PublishedRecordRules.IsPublishable(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount))
            .Select(candidate => new ReadyToPublishRecord(PublishedRecordKind.DocumentChange, candidate.Draft.DraftId,
                candidate.Draft.CurrentRevision.Headline, candidate.Draft.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber))
            .ToList();

    private static IReadOnlyList<ReadyToPublishRecord> ReadyStatements(IReadOnlyList<UnpublishedApprovedStatement> candidates) =>
        candidates
            .Where(candidate => candidate.Statement.CurrentRevision is not null &&
                PublishedRecordRules.IsPublishable(candidate.Statement.CurrentStatus, candidate.Statement.UnresolvedConcernCount))
            .Select(candidate => new ReadyToPublishRecord(PublishedRecordKind.Statement, candidate.Statement.CandidateId,
                candidate.Statement.CurrentRevision!.Headline, candidate.Statement.CurrentRevision.RevisionNumber,
                candidate.PublishedRevisionNumber))
            .ToList();
}
