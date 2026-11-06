namespace CivicLens.PublicApi.Records;

public sealed record RelatedPage(int Release, RelatedRecord[] Related, string? NextCursor);
