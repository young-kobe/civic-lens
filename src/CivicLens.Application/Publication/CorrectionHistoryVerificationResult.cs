namespace CivicLens.Application.Publication;

/// <summary>
/// <paramref name="VerifiedReleases"/> lists releases that this call finished with every record passed, including failures that earlier
/// calls carried in the cursor. <paramref name="Selection"/> is read after the last file read. A null cursor ends the walk.
/// </summary>
public sealed record CorrectionHistoryVerificationResult(int Examined, int Verified, int Failed,
    IReadOnlyList<CorrectionHistoryBackfillFailure> Failures, string? NextCursor,
    IReadOnlyList<CorrectionHistoryRelease> VerifiedReleases, IReadOnlyList<CorrectionHistorySkippedRelease> SkippedReleases,
    IReadOnlyList<CorrectionHistorySelectedRelease> Selection);
