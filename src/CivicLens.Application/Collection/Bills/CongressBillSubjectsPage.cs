namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillSubjectsPage(IReadOnlyList<string> Subjects, string? Next);
