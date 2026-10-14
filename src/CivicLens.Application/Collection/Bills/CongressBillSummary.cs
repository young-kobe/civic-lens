namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillSummary(
    string VersionCode,
    DateOnly ActionDate,
    string ActionDescription,
    DateTimeOffset UpdateDate,
    string Html);
