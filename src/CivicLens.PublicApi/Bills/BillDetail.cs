using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Bills;

/// <summary><c>LatestAction</c> is null when the active release pinned no status for the bill. <c>KeyTerms</c> is empty when the bill has none. <c>TextSentences</c> holds the sentences of the AI text summary in position order, summary first, and is empty when the bill has none. <c>KeyTermsLabel</c> is the label column of the key term view, and it is null when the bill has no key terms.
/// <c>CheckedAtUtc</c> is the stored approval time of the published revision, null until the entries backfill fills it. <c>SummaryFromCrs</c> is true when the stored summary is model text drafted from the CRS summary, with no bill text.</summary>
public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds, BillLatestAction? LatestAction, BillHistory? History,
    string? CrsSummaryText, string? CrsSummaryVersion, BillKeyTermData[] KeyTerms, BillTextSentenceData[] TextSentences)
{
    public string? KeyTermsLabel { get; init; }
    public DateTimeOffset? CheckedAtUtc { get; init; }
    public DateTimeOffset? FirstPublishedAtUtc { get; init; }
    public bool SummaryFromCrs { get; init; }
}
