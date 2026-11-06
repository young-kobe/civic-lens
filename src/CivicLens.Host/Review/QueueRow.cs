using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Host.Relationships;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Review;

internal sealed record QueueRow(PublishedRecordKind Kind, string RecordId, string Headline, string StatusLabel, Tone Tone, string Reason,
    DateTimeOffset? RevisedAtUtc)
{
    public static QueueRow Of(ReviewQueueItem item) => item.IsUnreadable
        ? new(item.Kind, item.RecordId, item.Headline, "Cannot read", Tone.Bad, item.Reason, item.LastRevisedAtUtc)
        : new(item.Kind, item.RecordId, item.LinkType is { } type ? RelationshipLabels.Title(type, item.Headline) : item.Headline,
            DraftStatusDisplay.Label(item.Status, item.OpenConcerns), DraftStatusDisplay.Tone(item.Status), item.Reason, item.LastRevisedAtUtc);

    public static QueueRow OfRejected(ReviewQueueItem item) => item.IsUnreadable
        ? Of(item)
        : new(item.Kind, item.RecordId, item.LinkType is { } type ? RelationshipLabels.Title(type, item.Headline) : item.Headline,
            DraftStatusDisplay.Label(item.Status, 0), DraftStatusDisplay.Tone(item.Status),
            item.RejectReason is { } reason ? $"Reason: {DraftStatusDisplay.Label(reason)}." : "A person rejected this record.", item.LastRevisedAtUtc);

    public static QueueRow OfUnmatchedApproved(ReviewQueueItem item) => item.IsUnreadable
        ? Of(item)
        : new(item.Kind, item.RecordId, RelationshipLabels.Title(item.LinkType!.Value, item.Headline), "Approved", Tone.Warn,
            RelationshipLabels.NoLongerMatches + " " + RelationshipLabels.CheckApprovedLink, null);

    public static QueueRow Of(BlockedPublishedRecord record, string stuckText) =>
        new(record.Kind, record.RecordId, record.Headline, "Cannot publish", Tone.Warn, stuckText, null);
}
