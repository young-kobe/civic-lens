namespace CivicLens.Core.Review;

public static class ReviewAuthor
{
    public const string AnalysisSubject = "civic-lens:analysis";

    public static bool IsAnalysis(string? subject) => string.Equals(subject, AnalysisSubject, StringComparison.Ordinal);
}
