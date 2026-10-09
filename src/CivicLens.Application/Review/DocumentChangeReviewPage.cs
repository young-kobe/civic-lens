using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record DocumentChangeReviewPage(ImmutableArray<DocumentChangeReviewListItem> Items,
    string? NewerCursor, string? OlderCursor)
{
    // Transitional: the Razor markup still reads NextCursor. Remove with that markup.
    public string? NextCursor => OlderCursor;
}
