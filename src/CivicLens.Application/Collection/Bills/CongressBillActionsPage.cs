namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillActionsPage(IReadOnlyList<CongressBillAction> Actions, string? Next);
