namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryBootstrapPage(IReadOnlyList<CorrectionHistoryBootstrapCandidate> Candidates,
    CorrectionHistoryBootstrapCursor? NextCursor);
