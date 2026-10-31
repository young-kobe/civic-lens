using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal sealed record QueueRow(PublishedRecordKind Kind, string RecordId, string Headline, string StatusLabel, Tone Tone, string Reason,
    DateTimeOffset? RevisedAtUtc)
{
    public static QueueRow Of(ReviewQueueItem item) => item.IsUnreadable
        ? new(item.Kind, item.RecordId, item.Headline, "Cannot read", Components.Ui.Tone.Bad, item.Reason, item.LastRevisedAtUtc)
        : new(item.Kind, item.RecordId, item.LinkType is { } type ? RelationshipLabels.Title(type, item.Headline) : item.Headline,
            DraftStatusDisplay.Label(item.Status, item.OpenConcerns), DraftStatusDisplay.Tone(item.Status), item.Reason, item.LastRevisedAtUtc);

    public static QueueRow OfUnmatchedApproved(ReviewQueueItem item) => item.IsUnreadable
        ? Of(item)
        : new(item.Kind, item.RecordId, RelationshipLabels.Title(item.LinkType!.Value, item.Headline), "Approved", Components.Ui.Tone.Warn,
            RelationshipLabels.NoLongerMatches + " Open the link and check it. Withdraw the approval if it is wrong.", null);

    public static QueueRow Of(BlockedPublishedRecord record, string stuckText) =>
        new(record.Kind, record.RecordId, record.Headline, "Cannot publish", Components.Ui.Tone.Warn, stuckText, null);
}
