namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillListPage(IReadOnlyList<CongressBillListItem> Bills, string? Next);
