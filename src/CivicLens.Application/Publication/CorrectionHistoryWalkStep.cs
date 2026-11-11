namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryWalkStep(PublicationReleaseSummary? Target, PublishedRecordReference? After,
    PublicationReleaseSummary? Next, CorrectionHistorySkippedRelease? Skipped);
