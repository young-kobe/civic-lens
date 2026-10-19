using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record ReviewTriagePage<TReview>(ImmutableArray<ReviewTriageRow<TReview>> Rows, string? NewerCursor, string? OlderCursor);
