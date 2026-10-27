using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Host.Actions;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedActionView
{
    public static IReadOnlyList<Fact> Facts(PublishedAction action, PublicationCatalog catalog) =>
    [
        new("Bill", ActionLabels.Bill(action.Congress, action.BillType, action.BillNumber)),
        new("Title", action.BillTitle),
        Fact.LinkList("Policy area", [PublishedRecordView.PolicyAreaLink(action.PolicyAreaId, action.PolicyArea ?? "None", catalog)]),
        PublishedRecordView.Issues("Tracked issues", action.TrackedIssueIds, catalog),
        new("Published", $"From the official record, checked {Moment.Full(action.ApprovedAtUtc)}; first published {Moment.Full(action.FirstPublishedAtUtc)}"),
        .. PublishedRecordLabels.Wording(action.TextOrigin),
        new("Revision", action.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedAction action, PublishedDocumentVersion source) =>
    [.. action.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];

    public static string SponsorshipAnchor(PublishedAction action, PublishedSponsorship sponsorship, int citationIndex) =>
        PublishedRecordView.CitationAnchor(action.Sponsorships.TakeWhile(item => !ReferenceEquals(item, sponsorship))
            .Sum(item => item.Citations.Length) + citationIndex);

    public static string PolicyAreaAnchor(PublishedAction action) => PublishedRecordView.CitationAnchor(SponsorshipCitationCount(action));

    public static string SummaryAnchor(PublishedAction action) =>
        PublishedRecordView.CitationAnchor(SponsorshipCitationCount(action) + (action.PolicyAreaCitation is null ? 0 : 1));

    private static int SponsorshipCitationCount(PublishedAction action) => action.Sponsorships.Sum(item => item.Citations.Length);
}
