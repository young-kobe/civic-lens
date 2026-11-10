namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryBackfillResult(int Examined, int Added, int AlreadyPresent, int Failed,
    IReadOnlyList<CorrectionHistoryBackfillFailure> Failures, string? NextCursor);
