using System.Collections.Immutable;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipReviewPage(ImmutableArray<RelationshipReviewItem> Items, string? NewerCursor, string? OlderCursor);
