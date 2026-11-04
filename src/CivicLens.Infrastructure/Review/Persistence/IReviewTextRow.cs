using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IReviewTextRow : IHeadlineSummaryRow
{
    ReviewTextOrigin? TextOrigin { get; set; }
    string? SuggestionHeadline { get; set; }
    string? SuggestionSummary { get; set; }
    string? SuggestionRunId { get; set; }
    string? SuggestionFingerprint { get; set; }
}
