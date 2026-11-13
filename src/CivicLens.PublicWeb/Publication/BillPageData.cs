using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// What the server rendered bill page shows. <c>Title</c> is the official title. <c>Headline</c> is the published record headline, which names the record in reports. <c>BillLine</c> is the first sentence of the CRS summary, or null.
/// <c>Status</c> is the plain meaning of the latest Congress.gov action, with the day of that action and the time Civic Lens read it. <c>StatusLabel</c> is the short label of the same action, or null when no rule matches it.
/// <c>Votes</c> are the shared vote rows of the roll calls that name the bill, with the chamber totals and the split badge. A row holds no cast, because the page names no member.
/// <c>SenateVoteGap</c> is true when the stepper has a Senate step after the introduction and no Senate roll call vote in the release names the bill.
/// <c>SummaryFromCrs</c> is true when the summary is model text drafted from the CRS summary. <c>CheckedAtUtc</c> is the approval time of the published revision.
/// </summary>
public sealed record BillPageData(
    int Release, string RecordId, int RevisionNumber, int Congress, string Number, string Title, string Headline, PublishedChamber Chamber,
    string Summary, PublishedTextOrigin TextOrigin, string? Label, DateOnly Introduced)
{
    /// <summary>The correction history of the shown revision in the active release, newest first. The correction banner shows the newest.</summary>
    public PublishedCorrection[] Corrections { get; init; } = [];

    public string? BillLine { get; init; }
    public string? PolicyArea { get; init; }
    public string? PublicLaw { get; init; }
    public DateOnly? BecameLawOn { get; init; }
    public string? Status { get; init; }
    public string? StatusLabel { get; init; }
    public DateOnly? StatusDate { get; init; }
    public DateTimeOffset? StatusReadAtUtc { get; init; }
    public BillSponsorData[] Sponsors { get; init; } = [];
    public BillCosponsorsData? Cosponsors { get; init; }
    public BillStepData[] Steps { get; init; } = [];
    public SharedVoteRowData[] Votes { get; init; } = [];
    public bool SenateVoteGap { get; init; }
    public BillIssueData[] Issues { get; init; } = [];
    public string? CrsText { get; init; }
    public string? CrsVersion { get; init; }
    public bool SummaryFromCrs { get; init; }
    public DateTimeOffset? CheckedAtUtc { get; init; }
    public DateTimeOffset? FirstPublishedAtUtc { get; init; }
    public required string CongressGovUrl { get; init; }
}
