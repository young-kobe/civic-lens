using System.Collections.Immutable;
using System.Text.Json;
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
            PublishedRecordBuilder.RequirePublishable(PublishedRecordKind.DocumentChange, draftId, revisionNumber,
                review.Decisions, review.UnresolvedConcerns);
            PublishedRecordBuilder.RequireNotPublishedAt(PublishedRecordKind.DocumentChange, draftId, revisionNumber, previous);
            var record = builder.Build(addition, previous.Find(PublishedRecordKind.DocumentChange, draftId), now);
            pending.Add(new(PublishedRecordKind.DocumentChange, record.RecordId, record.RevisionNumber, review.ReviewStateVersion,
                record.Headline, record.FirstPublishedAtUtc, JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions),
                await renderer.RenderRecordAsync(record, cancellationToken)));
        }
        return pending;
    }
}
