using System.Globalization;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublishedBriefView
{
    public static string ShortName(DateOnly weekStart) => $"{PublicColumn.WeekOf} {Moment.Day(weekStart)}";

    public const string CitedRecordsTitle = "Records this brief used";

    private const string SampleNote = "This brief is a sample, not a full account of the week.";

    public const string ReleaseTermKey = "release";

    private static string WeekStartText(DateOnly weekStart) => $"Starts Monday {Moment.Day(weekStart)}, New York time";

    public static string CitedRecordNote(PublishedBriefCitation citation) =>
        $"{PublishedRecordLabels.Kind(citation.Kind)}, revision {citation.RevisionNumber}";

    public static IReadOnlyList<Fact> Facts(PublishedBrief brief) =>
        Facts(brief.WeekStart, brief.RevisionNumber, brief.CutoffReleaseNumber, brief.Coverage, brief.ApprovedAtUtc, brief.FirstPublishedAtUtc);

    /// <summary>The fact rows of a brief. A time that the data does not hold gets no row, so the page never invents one.</summary>
    public static IReadOnlyList<Fact> Facts(DateOnly weekStart, int revisionNumber, int cutoffReleaseNumber, PublishedBriefCoverage coverage,
        DateTimeOffset? approvedAtUtc, DateTimeOffset? firstPublishedAtUtc)
    {
        var facts = new List<Fact>
        {
            new("Week", WeekStartText(weekStart)),
            new("Cutoff release", cutoffReleaseNumber.ToString(CultureInfo.InvariantCulture)) { TermKey = ReleaseTermKey },
            new("Records considered", coverage.Considered.ToString(CultureInfo.InvariantCulture)),
            new("Records sent to the drafting model", coverage.SentToModel.ToString(CultureInfo.InvariantCulture)),
            new("Records cited", coverage.Cited.ToString(CultureInfo.InvariantCulture))
        };
        if (approvedAtUtc is { } approved) facts.Add(new("Approved", Moment.Full(approved)));
        if (firstPublishedAtUtc is { } first) facts.Add(new("First published", Moment.Full(first)));
        facts.Add(new("Revision", revisionNumber.ToString(CultureInfo.InvariantCulture)));
        return facts;
    }

    /// <summary>The week as the heading names it: "2 Nov to 8 Nov 2026", with both years when the week crosses a year.</summary>
    public static string WeekRange(DateOnly weekStart)
    {
        var end = weekStart.AddDays(6);
        var start = weekStart.Year == end.Year ? weekStart.ToString("d MMM", CultureInfo.InvariantCulture) : Moment.Day(weekStart);
        return $"{start} to {Moment.Day(end)}";
    }

    public static string Sample(PublishedBrief brief) => Sample(brief.Coverage, brief.CutoffReleaseNumber);

    public static string Sample(PublishedBriefCoverage coverage, int cutoffReleaseNumber) =>
        $"{SampleBeforeRelease(coverage)}releases{SampleAfterRelease(cutoffReleaseNumber)}";

    /// <summary>The text of the sample note before the word "releases". The server page puts the glossary term around that word.</summary>
    public static string SampleBeforeRelease(PublishedBriefCoverage coverage) =>
        $"{SampleNote} It cites {coverage.Cited} of the {coverage.Considered} " +
        "new records that Civic Lens considered for the week. It reflects ";

    public static string SampleAfterRelease(int cutoffReleaseNumber) => $" up to release {cutoffReleaseNumber}.";

    public static string CitationAnchor(string citationId) => "citation-" + citationId;
}
