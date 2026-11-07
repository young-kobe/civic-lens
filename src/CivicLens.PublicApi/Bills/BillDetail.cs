namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when the active release pinned no status for the bill.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction, BillHistory? History,
    string? CrsSummaryText, string? CrsSummaryVersion);
