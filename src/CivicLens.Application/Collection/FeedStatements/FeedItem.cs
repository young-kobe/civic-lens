namespace CivicLens.Application.Collection.FeedStatements;

public sealed record FeedItem(int Index, string Link, string? Guid, string? Title, string? PublishedText,
    DateTimeOffset? Published, string Html);
