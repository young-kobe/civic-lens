using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

/// <summary>Owns the mapping between the headline, summary, origin, and suggestion of a revision and their columns.</summary>
internal static class ReviewTextRows
{
    public static void Fill(IReviewTextRow row, IHeadlineSummaryRevision revision)
    {
        row.Headline = revision.Headline;
        row.Summary = revision.Summary;
        row.TextOrigin = revision.TextOrigin;
        row.SuggestionHeadline = revision.Suggestion?.Headline;
        row.SuggestionSummary = revision.Suggestion?.Summary;
        row.SuggestionRunId = revision.Suggestion?.RunId;
        row.SuggestionFingerprint = revision.Suggestion?.Fingerprint;
    }

    public static ReviewTextSuggestion? SuggestionOf(IReviewTextRow row) => row.SuggestionHeadline is null
        ? null
        : new(row.SuggestionHeadline, row.SuggestionSummary!, row.SuggestionRunId!, row.SuggestionFingerprint!);
}
