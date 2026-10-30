using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record BlockedPublishedRecord(PublishedRecordKind Kind, string RecordId, string Headline,
    int PublishedRevisionNumber, ReviewStatus ReviewStatus, BlockedReason Reason)
{
    public static readonly string CorrectionLimitText =
        $"This record has {PublishedCorrection.MaximumNotedEntries} corrections with a note, the most it can hold. " +
        "A person revision cannot publish. Only a code-approved revision can.";

    public PublishedRecordReference Reference => new(Kind, RecordId);
}
