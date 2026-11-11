namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountBackfillResult(int Examined, int Added, int AlreadyPresent, int Skipped, int Failed,
    IReadOnlyList<ReleaseBillCountFailure> Failures, int? NextCursor);
