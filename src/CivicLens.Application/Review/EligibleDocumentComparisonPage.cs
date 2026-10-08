using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record EligibleDocumentComparisonPage(ImmutableArray<EligibleDocumentComparison> Items,
    string? NextCursor);
