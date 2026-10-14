namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillSummariesPage(IReadOnlyList<CongressBillSummary> Summaries, string? Next);
