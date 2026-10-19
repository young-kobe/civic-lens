namespace CivicLens.Core.Review;

public static class ReviewAuthor
{
    public const string AnalysisSubject = "civic-lens:analysis";

    public const string CodeSubject = "civic-lens:code";

    public static ReviewAuthorKind KindOf(string? subject) => IsAnalysis(subject) ? ReviewAuthorKind.Analysis : ReviewAuthorKind.Person;

    public static bool IsAnalysis(string? subject) => string.Equals(subject, AnalysisSubject, StringComparison.Ordinal);

    public static bool IsCode(string? subject) => string.Equals(subject, CodeSubject, StringComparison.Ordinal);

    public static bool IsReserved(string? subject) => IsAnalysis(subject) || IsCode(subject);
}

public enum ReviewAuthorKind { Person, Analysis }
