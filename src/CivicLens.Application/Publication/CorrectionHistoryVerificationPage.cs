namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryVerificationPage(IReadOnlyList<CorrectionHistoryVerificationCandidate> Candidates,
    string SelectionFingerprint, CorrectionHistoryVerificationCursor? NextCursor, bool StaleCursor = false);
