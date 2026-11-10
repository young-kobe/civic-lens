namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryVerificationResult(int Examined, int Verified, int Failed,
    IReadOnlyList<CorrectionHistoryBackfillFailure> Failures, string? NextCursor, bool StaleCursor)
{
    public string SelectionFingerprint { get; init; } = "";
}
