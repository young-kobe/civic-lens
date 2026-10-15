using System.Globalization;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedActionView
{
    public static IReadOnlyList<Fact> Facts(PublishedAction action) =>
    [
        new("Bill", ActionLabels.Bill(action.Congress, action.BillType, action.BillNumber)),
        new("Title", action.BillTitle),
        new("Policy area", action.PolicyArea ?? "None"),
        Fact.List("Tracked issues", action.TrackedIssueIds),
        new("Published", $"Approved {Moment.Full(action.ApprovedAtUtc)}; first published {Moment.Full(action.FirstPublishedAtUtc)}"),
        new("Revision", action.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedAction action, PublishedDocumentVersion source) =>
    [.. CitationsOf(action).Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];

    private static IEnumerable<PublishedCitation> CitationsOf(PublishedAction action) =>
        action.Sponsorships.SelectMany(sponsorship => sponsorship.Citations)
            .Concat(action.PolicyAreaCitation is { } area ? [area] : []).Concat(action.SummaryCitation is { } summary ? [summary] : []);
}
