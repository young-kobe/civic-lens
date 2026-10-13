namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillCosponsorsPage(IReadOnlyList<CongressBillCosponsor> Cosponsors, string? Next);
