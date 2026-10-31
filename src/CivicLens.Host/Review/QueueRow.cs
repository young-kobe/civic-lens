using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal sealed record QueueRow(PublishedRecordKind Kind, string RecordId, string Headline, string StatusLabel, Tone Tone, string Reason,
    DateTimeOffset? RevisedAtUtc)
{
    public static QueueRow Of(ReviewQueueItem item) =>
        new(item.Kind, item.RecordId, item.Headline, DraftStatusDisplay.Label(item.Status, item.OpenConcerns), DraftStatusDisplay.Tone(item.Status),
            item.Reason, item.LastRevisedAtUtc);

    public static QueueRow Of(BlockedPublishedRecord record, string stuckText) =>
        new(record.Kind, record.RecordId, record.Headline, "Cannot publish", Components.Ui.Tone.Warn, stuckText, null);
}
