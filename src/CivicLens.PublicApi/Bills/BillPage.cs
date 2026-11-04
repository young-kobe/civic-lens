namespace CivicLens.PublicApi.Bills;

public sealed record BillPage(int Release, BillRow[] Bills, string? NextCursor);
