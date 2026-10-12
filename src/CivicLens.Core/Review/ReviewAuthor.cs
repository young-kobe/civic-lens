namespace CivicLens.Core.Review;

public static class ReviewAuthor
{
    public const string AnalysisSubject = "civic-lens:analysis";

    public static ReviewAuthorKind KindOf(string? subject) => IsAnalysis(subject) ? ReviewAuthorKind.Analysis : ReviewAuthorKind.Person;

    public static bool IsAnalysis(string? subject) => string.Equals(subject, AnalysisSubject, StringComparison.Ordinal);
}

public enum ReviewAuthorKind { Person, Analysis }
