namespace CivicLens.Application.Collection.Processing;

public sealed record CollectionChangeEvent(string JobId, string SourceId, string ComparisonId, DateTimeOffset OccurredAt);
