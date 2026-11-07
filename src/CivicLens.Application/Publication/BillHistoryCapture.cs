namespace CivicLens.Application.Publication;

/// <summary>Identifies a completed collection chain and its capture observation time. The body stays in the store.</summary>
public sealed record BillHistoryCapture(string DetailJobId, string SourceId, int Congress, string BillType, int BillNumber,
    DateTimeOffset ObservedAtUtc, int ActionCount);
