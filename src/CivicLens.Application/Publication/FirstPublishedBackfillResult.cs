namespace CivicLens.Application.Publication;

public sealed record FirstPublishedBackfillResult(int Examined, int Added, int AlreadyPresent);
