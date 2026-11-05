namespace CivicLens.Application.Spending;

public sealed record MonthlyCapRevision(int RevisionNumber, long CapMicros, string AuthorSubject, DateTimeOffset RecordedAt);
