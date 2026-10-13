namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillAction(
    DateOnly ActionDate,
    TimeOnly? ActionTime,
    string Text,
    string? Type,
    string? ActionCode,
    int? SourceSystemCode,
    string? SourceSystemName);
