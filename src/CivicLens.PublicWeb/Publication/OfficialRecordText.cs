using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the words and the paths of the record page of an official. The static page of the same name holds the approved title, description, back link,
/// empty state, and "Rows are newest first." The official page and the record page both build their links here.
/// </summary>
public static class OfficialRecordText
{
    public const string EmptyTitle = "No records yet";
    public const string EmptyText = "There are no records here.";
    public const string TabsLabel = "Filter records";
    public const string PagerLabel = "Records pages";
    public const string OfficesAnchor = "offices";

    /// <summary>The value of the <c>kind</c> query parameter of a tab.</summary>
    public static string Query(OfficialRecordKind kind) => kind switch
    {
        OfficialRecordKind.Votes => "votes",
        OfficialRecordKind.Bills => "bills",
        OfficialRecordKind.Statements => "statements",
        OfficialRecordKind.ExecutiveActions => "executive-actions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no query value.")
    };

    public static string TabLabel(OfficialRecordKind kind) => kind switch
    {
        OfficialRecordKind.Votes => PublishedRecordLabels.KindPlural(PublishedRecordKind.Vote),
        OfficialRecordKind.Bills => "Bills",
        OfficialRecordKind.Statements => PublishedRecordLabels.KindPlural(PublishedRecordKind.Statement),
        OfficialRecordKind.ExecutiveActions => PublishedRecordLabels.KindPlural(PublishedRecordKind.ExecutiveAction),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no tab label.")
    };

    /// <summary>The path of the record page of an official. A kind selects a tab and a cursor selects a later page of it.</summary>
    public static string Path(string rootPath, string officialId, OfficialRecordKind? kind = null, string? cursor = null)
    {
        var query = new List<string>();
        if (kind is { } selected) query.Add("kind=" + Query(selected));
        if (cursor is not null) query.Add("cursor=" + Uri.EscapeDataString(cursor));
        return rootPath + PublicationProtocol.OfficialRecordPagePath(officialId) + (query.Count == 0 ? "" : "?" + string.Join('&', query));
    }

    public static string Heading(string name) => $"All records of {name}";

    /// <summary>The accessible name of the section that holds the tabs and the rows.</summary>
    public static string SectionLabel(string name) => $"The record of {name}";

    public static string Description(string name) => $"Every published record of {name}: statements, bills, and votes, with a tab for each kind.";

    public static string BackToOfficial(string name) => $"Back to {name}";

    /// <summary>The line above a list. It names who the records belong to, what the list counts, and its time frame.</summary>
    public static string CountLine(OfficialRecordKind kind, int count, string name) => kind switch
    {
        OfficialRecordKind.Votes => $"This list holds {count} {Noun(count, "vote")} of {name} in this release: roll calls on passage, amendments, conference reports, veto overrides and nominations. Rows are newest first.",
        OfficialRecordKind.Bills => $"This list holds {count} {(count == 1 ? "bill or resolution" : "bills and resolutions")} that {name} sponsored in this release. Rows are newest first.",
        OfficialRecordKind.Statements => $"This list holds {count} {Noun(count, "statement")} that name {name} as a speaker in this release. Rows are newest first.",
        OfficialRecordKind.ExecutiveActions => $"This list holds {count} executive {Noun(count, "action")} that {name} signed in this release. Rows are newest first.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no count line.")
    };

    public static string PagerSummary(int shown, int total) => $"{shown} on this page. {total} in all.";

    public static string OfficesSummary(int count) => $"Offices held ({count} {Noun(count, "term")})";

    public static string Office(OfficialOfficeData office) => string.Join(", ", new[]
    {
        office.Seat, office.Party, $"{Moment.Day(office.StartsOn)} to {(office.EndsOn is { } end ? Moment.Day(end) : "Present")}"
    }.OfType<string>());

    private static string Noun(int count, string singular) => count == 1 ? singular : singular + "s";
}
