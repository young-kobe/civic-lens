namespace CivicLens.Application.Publication;

/// <summary>
/// One snapshot page of one release. <paramref name="Release"/> is null when no selected release is left to walk.
/// <paramref name="NextRelease"/> names the following selected release when <paramref name="ReleaseCompleted"/> is true.
/// </summary>
public sealed record CorrectionHistoryVerificationPage(PublicationReleaseSummary? Release,
    IReadOnlyList<CorrectionHistoryVerificationCandidate> Candidates, bool ReleaseCompleted, PublicationReleaseSummary? NextRelease,
    CorrectionHistorySkippedRelease? Skipped = null);
