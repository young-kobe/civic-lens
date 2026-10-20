namespace CivicLens.PublicApi.Search;

public sealed record SearchPage(int Release, RecordHit[] Hits, string? NextCursor);
