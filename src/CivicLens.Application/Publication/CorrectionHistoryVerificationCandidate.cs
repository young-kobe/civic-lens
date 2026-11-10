namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryVerificationCandidate(PublicationReleaseSummary Release, StoredReleaseRecord Record,
    StoredCorrectionHistory? StoredHistory, string? StoredHistoryError = null);
