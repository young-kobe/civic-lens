using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class DocumentChangeRecordLoader(IDocumentChangeReviewStore reviews, IReleaseRenderer renderer,
    PublishedRecordBuilder builder)
{
    public async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> draftIds, PreviousRelease previous, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (draftIds.IsEmpty) return [];
        var found = await reviews.GetForPublicationAsync(draftIds, cancellationToken);
        var pending = new List<PendingRecord>(draftIds.Length);
        foreach (var draftId in draftIds)
        {
            var addition = found.GetValueOrDefault(draftId)
                ?? throw new ArgumentException($"Draft {draftId} does not exist.", nameof(draftIds));
            var review = addition.Review;
            var revisionNumber = review.CurrentRevision.RevisionNumber;
            PublishedRecordRules.RequirePublishable(PublishedRecordKind.DocumentChange, draftId, revisionNumber,
                review.Decisions, review.UnresolvedConcerns);
            PublishedRecordRules.RequireNotPublishedAt(PublishedRecordKind.DocumentChange, draftId, revisionNumber, previous);
            var record = builder.Build(addition, previous.Find(PublishedRecordKind.DocumentChange, draftId), now);
            pending.Add(PendingRecord.Create(PublishedRecordKind.DocumentChange, review.ReviewStateVersion, record, record.RecordId,
                record.RevisionNumber, record.Headline, record.FirstPublishedAtUtc,
                await renderer.RenderRecordAsync(record, cancellationToken)));
        }
        return pending;
    }
}
