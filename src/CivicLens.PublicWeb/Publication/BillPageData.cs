using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// What the server rendered bill page shows. <c>Title</c> is the official title. <c>Headline</c> is the published record headline, which names the record in reports. <c>BillLine</c> is the first sentence of the CRS summary, or null.
/// <c>Status</c> is the plain meaning of the latest Congress.gov action, with the day of that action and the time Civic Lens read it. <c>StatusLabel</c> is the short label of the same action, or null when no rule matches it.
/// <c>Votes</c> are the shared vote rows of the roll calls that name the bill, with the chamber totals and the split badge. A row holds no cast, because the page names no member.
/// <c>SenateVoteGap</c> is true when the stepper has a Senate step and no Senate roll call vote names the bill.
/// </summary>
public sealed record BillPageData(
    int Release, string RecordId, int RevisionNumber, int Congress, string Number, string Title, string Headline, string Chamber,
    string Summary, PublishedTextOrigin TextOrigin, string? Label, DateOnly Introduced)
{
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
    public required string CongressGovUrl { get; init; }
}
