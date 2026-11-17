namespace CivicLens.Application.Collection.Bills;

/// <summary>The outcome of one bounded page of historical bill action chains. NextCursor is the last examined detail job ID when more work may remain.</summary>
public sealed record BillActionsBackfillResult(int Examined, int Recorded, int Failed, string? NextCursor);
