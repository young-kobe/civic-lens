using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record EligibleDocumentComparisonPage(ImmutableArray<EligibleDocumentComparison> Items,
    string? NewerCursor, string? OlderCursor)
{
    // Transitional: the Razor markup still reads NextCursor. Remove with that markup.
    public string? NextCursor => OlderCursor;
}
