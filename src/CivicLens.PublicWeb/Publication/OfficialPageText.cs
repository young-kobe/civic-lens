using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words and plain rules of the server-rendered official and President pages. Fixed text comes from the Mosaic v2 official and President pages.</summary>
public static class OfficialPageText
{
    public const string LinkMeaning = "A reviewer approved each link. A link does not mean the vote or bill matches the statement.";

    public const string NoLinks = "No reviewed links between their statements and their bills or votes yet.";

    public const string StatementsHeading = "What they said and how they voted";

    public const string BillCountsUnavailable = "Bill counts for this Congress are not available in this release.";

    public const string NothingToShow = "This page has no votes, bills or linked statements to show yet.";

    public const string ExecutiveBoxTitle = "What these documents are";

    public const string ExecutiveBox = "The President signs these documents. The Federal Register prints them, and Civic Lens copies each title and date from there. A title alone does not show what a document does, so each page links to the full text.";

    /// <summary>The lede of the header: party, role, place, and the end of the current term. The rules are <see cref="OfficeLede"/>, and no current term end shows for a former member or when the release date is unknown.</summary>
    public static string Lede(OfficialPageData data)
    {
        var known = data.PublishedOn is not null;
        var ended = data.PublishedOn is { } publishedOn && OfficeLede.HasEnded(data.TermEndsOn, publishedOn);
        var role = data.Kind switch
        {
            PublishedOfficeKind.Senator => "senator",
            PublishedOfficeKind.Representative => "representative",
            _ => "official"
        };
        var place = OfficeLede.LedePlace(data.State, data.District);
        var text = OfficeLede.Of(ended, data.Party, role, place, data.Caucus) + ".";
        return known && !ended && data.TermEndsOn is { } day ? $"{text} Current term ends {Moment.Day(day)}." : text;
    }

    /// <summary>The party figure of the page. One figure covers the page, so a member with two party lines shows the newest.</summary>
    public static OfficialPartyLineData? MainPartyLine(OfficialPageData data) =>
        data.PartyLines.OrderByDescending(line => line.LastVotedOn).ThenBy(line => line.Chamber).FirstOrDefault();

    public static string VotedOn(OfficialAttendanceData figure) =>
        $"roll calls voted on, of {figure.Held} {figure.Chamber} roll calls on this site in the {CongressCalendar.Ordinal(figure.Congress)} Congress. The count includes procedural votes.";

    public static string PartyLineScope(OfficialPartyLineData line) =>
        $"cast with most {PublishedPartyNames.PartyOf(line.Chamber, line.Party)}, in the {CongressCalendar.Ordinal(line.Congress)} Congress.";

    public static string BillsSponsored(OfficialPageData data) =>
        $"bills and resolutions sponsored in the {CongressCalendar.Ordinal(data.BillsCongress!.Value)} Congress, counted in this release";

    public static string MarkName(OfficialMarkData mark) => mark switch
    {
        { IsMissed: true } => PlainVoteWords.Cast(PublishedOfficialVote.NotVoting),
        { Party: { } party } => "Voted " + VoteBreakers.AgainstMost(PublishedPartyNames.Members(party)),
        _ => throw new InvalidDataException("A mark that is not a missed vote names the party that it broke from.")
    };

    public static string AgainstPartyLegend(OfficialPartyLineData line) =>
        $"{line.AgainstParty} {(line.AgainstParty == 1 ? "vote" : "votes")} {VoteBreakers.AgainstMost(PublishedPartyNames.PartyOf(line.Chamber, line.Party))}";

    public static string ExecutivePlural(PublishedExecutiveActionKind kind, int count) => (kind, count) switch
    {
        (PublishedExecutiveActionKind.ExecutiveOrder, 1) => "executive order",
        (PublishedExecutiveActionKind.ExecutiveOrder, _) => "executive orders",
        (PublishedExecutiveActionKind.Proclamation, 1) => "proclamation",
        (PublishedExecutiveActionKind.Proclamation, _) => "proclamations",
        (PublishedExecutiveActionKind.Notice, 1) => "presidential notice",
        (PublishedExecutiveActionKind.Notice, _) => "presidential notices",
        (PublishedExecutiveActionKind.Memorandum, 1) => "memorandum",
        (PublishedExecutiveActionKind.Memorandum, _) => "memorandums",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no plural.")
    };

    public static string ExecutiveMeaning(PublishedExecutiveActionKind kind) => kind switch
    {
        PublishedExecutiveActionKind.ExecutiveOrder => "An executive order is a signed direction from the President to federal agencies. It can change how agencies carry out laws.",
        PublishedExecutiveActionKind.Proclamation => "A proclamation is a public statement from the President. Many name a day or month. Some, such as tariff changes, have legal effect.",
        PublishedExecutiveActionKind.Notice => "A notice gives public information. One example is the yearly renewal of a national emergency.",
        PublishedExecutiveActionKind.Memorandum => "A memorandum is a written direction to a federal agency or official. It often has less detail than an order.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no meaning text.")
    };

    public static string ExecutiveAnchor(PublishedExecutiveActionKind kind) => "type-" + kind.ToString().ToLowerInvariant();

    public static string ExecutiveHeading(PublishedExecutiveActionKind kind, int count) =>
        $"{char.ToUpperInvariant(ExecutivePlural(kind, 2)[0])}{ExecutivePlural(kind, 2)[1..]} ({count})";
}
