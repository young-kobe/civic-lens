namespace CivicLens.PublicApi.Bills;

public sealed record ActionHistoryPage(int Release, BillHistory? History, BillActionRow[] Actions, string? NextCursor);
