using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record BlockedPublishedRecord(PublishedRecordKind Kind, string RecordId, string Headline,
    int PublishedRevisionNumber, ReviewStatus ReviewStatus, BlockedReason Reason)
{
    private static readonly string AtLimit = $"{PublishedCorrection.MaximumNotedEntries} corrections with a note, the most it can hold.";

    public PublishedRecordReference Reference => new(Kind, RecordId);

    /// <summary>Tells the owner why the record cannot move on. It is null when a withdrawal frees the record.</summary>
    public string? StuckText() => Reason switch
    {
        BlockedReason.CorrectionLimit when CodeApproval.CanApprove(Kind) =>
            $"This record has {AtLimit} A person revision cannot publish. Only a code-approved revision can.",
        BlockedReason.CorrectionLimit => $"This record has {AtLimit} No new revision can publish.",
        BlockedReason.WithdrawnAtCorrectionLimit => $"This record is off the site and has {AtLimit} It cannot return to the site.",
        _ => null
    };
}
