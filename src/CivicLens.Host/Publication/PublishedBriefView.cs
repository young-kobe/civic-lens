using System.Globalization;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedBriefView
{
    public static IReadOnlyList<Fact> Facts(PublishedBrief brief) =>
    [
        new("Week", $"Starts Monday {Moment.Iso(brief.WeekStart)}, New York time"),
        new("Cutoff release", brief.CutoffReleaseNumber.ToString(CultureInfo.InvariantCulture)),
        new("Records considered", brief.Coverage.Considered.ToString(CultureInfo.InvariantCulture)),
        new("Records sent to the drafting model", brief.Coverage.SentToModel.ToString(CultureInfo.InvariantCulture)),
        new("Records cited", brief.Coverage.Cited.ToString(CultureInfo.InvariantCulture)),
        new("Published", $"Approved {Moment.Full(brief.ApprovedAtUtc)}; first published {Moment.Full(brief.FirstPublishedAtUtc)}"),
        new("Revision", brief.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static string Sample(PublishedBrief brief) =>
        $"This brief is a sample, not a full account of the week. It cites {brief.Coverage.Cited} of the {brief.Coverage.Considered} " +
        $"new records that Civic Lens considered for the week. It reflects releases up to release {brief.CutoffReleaseNumber}.";

    public static string CitationAnchor(string citationId) => "citation-" + citationId;
}
