namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountVerificationResult(int Examined, int Verified, int Skipped, int Failed,
    IReadOnlyList<ReleaseBillCountFailure> Failures, string? NextCursor, bool StaleCursor)
{
    public string SelectionFingerprint { get; init; } = "";
}
