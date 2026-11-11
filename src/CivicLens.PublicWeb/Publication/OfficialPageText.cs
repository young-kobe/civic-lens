using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words and plain rules of the server-rendered official and President pages. Fixed text comes from the Mosaic v2 official and President pages.</summary>
public static class OfficialPageText
{
    public const string LinkMeaning = "A reviewer approved each link. A link does not mean the vote or bill matches the statement.";

    public const string NoLinks = "No reviewed links between their statements and their bills or votes yet.";

    public const string StatementsHeading = "What they said and how they voted";

    public const string NoPageRecords = "No votes, bills or statements are published for this official yet.";

    public const string ExecutiveBoxTitle = "What these documents are";

    public const string ExecutiveBox = "The President signs these documents. The Federal Register prints them, and Civic Lens copies each title and date from there. A title alone does not show what a document does, so each page links to the full text.";

    public const string SplitVoteKey = "split-vote";

    /// <summary>The lede of the header: party, role, place, and the end of the current term.</summary>
    public static string Lede(OfficialPageData data)
    {
        var party = data.Party == PublishedPartyMajority.Democrat ? "Democratic" : data.Party;
        var role = data.Kind switch
        {
            PublishedOfficeKind.Senator => "senator",
            PublishedOfficeKind.Representative => "representative",
            _ => "official"
        };
        var text = string.Join(' ', new[] { party, role }.OfType<string>()) + Place(data) + Caucus(data) + ".";
        var end = data.TermEndsOn is { } day ? $" Current term ends {Moment.Day(day)}." : "";
        return char.ToUpperInvariant(text[0]) + text[1..] + end;
    }

    private static string Place(OfficialPageData data) => data switch
    {
        { State: null } => "",
        { District: { } district } => $" for {UsStates.NameOf(data.State!)}, district {district}",
        _ => $" for {UsStates.NameOf(data.State!)}"
    };

    private static string Caucus(OfficialPageData data) =>
        data.Caucus is { } caucus && data.Party is { } party && caucus != party ? $", caucuses with {PublishedPartyNames.Members(caucus)}" : "";

    /// <summary>The party figure of the page. One figure covers the page, so a member with two party lines shows the newest.</summary>
    public static OfficialPartyLineData? MainPartyLine(OfficialPageData data) =>
        data.PartyLines.OrderByDescending(line => line.LastVotedOn).ThenBy(line => line.Chamber).FirstOrDefault();

    public static string VotedOn(OfficialAttendanceData figure) =>
        $"roll calls voted on, of {figure.Held} {figure.Chamber} roll calls on this site in the {CongressCalendar.Ordinal(figure.Congress)} Congress. The count includes procedural votes.";

    public static string PartyLineScope(OfficialPartyLineData line) =>
        $"cast with most {PublishedPartyNames.PartyOf(line.Chamber, line.Party)}, in the {CongressCalendar.Ordinal(line.Congress)} Congress.";

    public static string BillsSponsored(OfficialPageData data) =>
        $"bills and resolutions sponsored in the {CongressCalendar.Ordinal(data.BillsCongress!.Value)} Congress, counted in this release";

    public static string MarkName(OfficialMarkData mark) => mark.IsMissed
        ? PlainVoteWords.Cast(PublishedOfficialVote.NotVoting)
        : $"Voted against most {PublishedPartyNames.Members(mark.Party ?? "of the party")}";

    /// <summary>The short badge of a bill status. It is the first clause of the shared plain meaning, and a latest action that no known shape matches gets no badge.</summary>
    public static string? StatusBadge(OfficialBillData bill)
    {
        if (bill.LatestAction is not { } action) return null;
        var plain = BillStatusMeaning.Plain(action, bill.BillType);
        var unmatched = action.EndsWith('.') ? action : action + ".";
        if (plain == unmatched) return null;
        var end = plain.IndexOfAny([':', '.']);
        return end < 0 ? plain : plain[..end];
    }

    public static string BillTitle(OfficialBillData bill) => bill.BillLine ?? bill.BillTitle;

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
