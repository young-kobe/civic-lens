namespace CivicLens.Core.Review;

public interface IHeadlineSummaryRevision : IReviewRevision
{
    string Headline { get; }
    string Summary { get; }
    ReviewTextOrigin TextOrigin { get; }
    ReviewTextSuggestion? Suggestion { get; }
}
