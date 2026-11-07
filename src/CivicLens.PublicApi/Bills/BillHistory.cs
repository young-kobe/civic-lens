namespace CivicLens.PublicApi.Bills;

public sealed record BillHistory(DateTimeOffset ObservedAtUtc, int ActionCount);
