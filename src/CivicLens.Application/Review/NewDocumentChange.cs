using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record NewDocumentChange(string ComparisonId, string SourceId, string RequestedUrl,
    DateTimeOffset ObservedAtUtc, int PassageCount, DocumentComparisonHunk FirstPassage);
