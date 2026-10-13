using System.Collections.Immutable;
using CivicLens.Application.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class StatementRecordLoader(IStatementReviewStore statements, IReleaseRenderer renderer,
    PublishedStatementBuilder builder)
{
    public async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> candidateIds, PreviousRelease previous, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (candidateIds.IsEmpty) return [];
        var found = await statements.GetForPublicationAsync(candidateIds, cancellationToken);
        var pending = new List<PendingRecord>(candidateIds.Length);
        foreach (var candidateId in candidateIds)
        {
            var addition = found.GetValueOrDefault(candidateId)
                ?? throw new ArgumentException($"Statement {candidateId} does not exist.", nameof(candidateIds));
            var review = addition.Review;
            var revision = review.CurrentRevision
                ?? throw new ArgumentException($"Statement {candidateId} has no revision.", nameof(candidateIds));
            PublishedRecordRules.RequirePublishable(PublishedRecordKind.Statement, candidateId, revision.RevisionNumber,
                review.Decisions, review.UnresolvedConcerns);
            PublishedRecordRules.RequireNotPublishedAt(PublishedRecordKind.Statement, candidateId, revision.RevisionNumber, previous);
            var record = builder.Build(addition, previous.Find(PublishedRecordKind.Statement, candidateId), now);
            pending.Add(PendingRecord.Create(PublishedRecordKind.Statement, review.ReviewStateVersion, record, record.RecordId,
                record.RevisionNumber, record.Headline, record.FirstPublishedAtUtc,
                await renderer.RenderStatementAsync(record, cancellationToken)));
        }
        return pending;
    }
}
