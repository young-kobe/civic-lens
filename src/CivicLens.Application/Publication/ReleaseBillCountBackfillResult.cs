namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountBackfillResult(int Examined, int Added, int AlreadyPresent, int Skipped, int Failed,
    IReadOnlyList<ReleaseBillCountFailure> Failures, string? NextCursor, bool StaleCursor)
{
    public string SelectionFingerprint { get; init; } = "";
}
