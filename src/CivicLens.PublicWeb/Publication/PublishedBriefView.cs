using System.Globalization;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublishedBriefView
{
    public static string ShortName(DateOnly weekStart) => $"{PublicColumn.WeekOf} {Moment.Day(weekStart)}";

    public const string CitedRecordsTitle = "Cited records";

    public const string SampleNote = "This brief is a sample, not a full account of the week.";

    public static string WeekStartText(DateOnly weekStart) => $"Starts Monday {Moment.Day(weekStart)}, New York time";

    public static string CitedRecordNote(PublishedBriefCitation citation) =>
        $"{PublishedRecordLabels.Kind(citation.Kind)}, revision {citation.RevisionNumber}";

    public static IReadOnlyList<Fact> Facts(PublishedBrief brief) =>
    [
        new("Week", WeekStartText(brief.WeekStart)),
        new("Cutoff release", brief.CutoffReleaseNumber.ToString(CultureInfo.InvariantCulture)),
        new("Records considered", brief.Coverage.Considered.ToString(CultureInfo.InvariantCulture)),
        new("Records sent to the drafting model", brief.Coverage.SentToModel.ToString(CultureInfo.InvariantCulture)),
        new("Records cited", brief.Coverage.Cited.ToString(CultureInfo.InvariantCulture)),
        new("Published", $"Approved {Moment.Full(brief.ApprovedAtUtc)}; first published {Moment.Full(brief.FirstPublishedAtUtc)}"),
        new("Revision", brief.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static string Sample(PublishedBrief brief) =>
        $"{SampleNote} It cites {brief.Coverage.Cited} of the {brief.Coverage.Considered} " +
        $"new records that Civic Lens considered for the week. It reflects releases up to release {brief.CutoffReleaseNumber}.";

    public static string CitationAnchor(string citationId) => "citation-" + citationId;
}
