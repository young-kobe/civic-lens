namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when the active release pinned no status for the bill. <c>KeyTerms</c> is empty when the bill has none. <c>KeyTermsLabel</c> is the label column of the key term view, and it is null when the bill has no key terms.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction, BillHistory? History,
    string? CrsSummaryText, string? CrsSummaryVersion, BillKeyTerm[] KeyTerms)
{
    public string? KeyTermsLabel { get; init; }
}
