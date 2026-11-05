namespace CivicLens.Application.Spending;

public sealed record MonthlyCapRevision(int RevisionNumber, long CapMicros, string AuthorSubject, DateTimeOffset RecordedAt);

public sealed record ModelPriceRevision(int RevisionNumber, ModelPrice Price, string AuthorSubject, DateTimeOffset RecordedAt);
