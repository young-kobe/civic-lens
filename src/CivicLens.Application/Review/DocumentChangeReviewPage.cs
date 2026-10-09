using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record DocumentChangeReviewPage(ImmutableArray<DocumentChangeReviewListItem> Items,
    string? NewerCursor, string? OlderCursor);
