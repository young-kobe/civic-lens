namespace CivicLens.Core.Review;

public static class ReviewAuthor
{
    public const string AnalysisSubject = "civic-lens:analysis";

    public const string CodeSubject = "civic-lens:code";

    public const int MaximumSubjectLength = 256;

    public static ReviewAuthorKind KindOf(string? subject) => IsAnalysis(subject) ? ReviewAuthorKind.Analysis : IsCode(subject) ? ReviewAuthorKind.Code : ReviewAuthorKind.Person;

    public static bool IsAnalysis(string? subject) => string.Equals(subject, AnalysisSubject, StringComparison.Ordinal);

    public static bool IsCode(string? subject) => string.Equals(subject, CodeSubject, StringComparison.Ordinal);

    public static bool IsReserved(string? subject) => IsAnalysis(subject) || IsCode(subject);

    public static bool IsValidSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) && subject.Length <= MaximumSubjectLength;

    public static bool IsValidRevisionHeader(int revisionNumber, string? authorSubject, DateTimeOffset createdAtUtc) =>
        revisionNumber >= 1 && IsValidSubject(authorSubject) && createdAtUtc.Offset == TimeSpan.Zero;
}

public enum ReviewAuthorKind { Person, Analysis, Code }
