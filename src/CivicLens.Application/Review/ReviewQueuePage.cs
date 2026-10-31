using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record ReviewQueuePage(ImmutableArray<ReviewQueueItem> Items, int Total, string? NewerCursor, string? OlderCursor);
