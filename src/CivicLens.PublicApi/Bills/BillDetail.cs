namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when no gate row of the bill holds a latest action.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction);
