namespace CivicLens.Application.Collection.Jobs;

/// <summary>A configured source and the exact URL its scheduled check requests.</summary>
public sealed record SourceCheckTarget(string SourceId, string Url);
