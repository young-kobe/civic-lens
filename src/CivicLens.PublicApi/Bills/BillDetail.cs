namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when the active release pinned no status for the bill. <c>CheckedAtUtc</c> is the time of the approval of the published revision.
/// <c>SummaryFromCrs</c> is true when the stored summary is model text drafted from the CRS summary, with no bill text.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction, BillHistory? History,
    string? CrsSummaryText, string? CrsSummaryVersion)
{
    public DateTimeOffset? CheckedAtUtc { get; init; }
    public DateTimeOffset? FirstPublishedAtUtc { get; init; }
    public bool SummaryFromCrs { get; init; }
}
