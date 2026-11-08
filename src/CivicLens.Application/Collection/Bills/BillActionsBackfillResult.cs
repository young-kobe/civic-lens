namespace CivicLens.Application.Collection.Bills;

public sealed record BillActionsBackfillResult(int Examined, int Recorded, int Failed);
