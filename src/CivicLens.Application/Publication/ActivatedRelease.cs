namespace CivicLens.Application.Publication;

public sealed record ActivatedRelease(PublicationReleaseSummary Summary, IReadOnlyList<PublishedRecordReference> RestoredWithdrawals,
    IReadOnlyList<PublishedRecordReference> WithdrawnAgain, IReadOnlyList<BlockedPublishedRecord> Blocked);
