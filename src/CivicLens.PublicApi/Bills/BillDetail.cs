namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when the active release pinned no status for the bill. <c>KeyTerms</c> is empty when the bill has none.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction, BillHistory? History,
    string? CrsSummaryText, string? CrsSummaryVersion, BillKeyTerm[] KeyTerms);
