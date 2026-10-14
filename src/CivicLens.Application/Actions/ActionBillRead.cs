namespace CivicLens.Application.Actions;

public sealed record ActionBillRead(ActionBillSnapshot? Snapshot, string? ErrorCode);
