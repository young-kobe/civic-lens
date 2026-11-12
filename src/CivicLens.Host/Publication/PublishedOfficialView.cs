using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Host.Actions;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedOfficialView
{
    public static string Office(PublishedOfficeTerm term) => Role(term) + Place(term, OfficeLede.Place);

    private static string Role(PublishedOfficeTerm term) => term.Office switch
    {
        MemberRosterParser.Senate => "Senator",
        MemberRosterParser.House => "Representative",
        ExecutiveRosterParser.President => "President",
        ExecutiveRosterParser.VicePresident => "Vice President",
        _ => term.Office
    };

    private static string Place(PublishedOfficeTerm term, Func<string?, int?, string> place) => term.Office switch
    {
        MemberRosterParser.Senate => place(term.State, null),
        MemberRosterParser.House => place(term.State, term.District),
        _ => ""
    };

    public static string CardOffice(PublishedOfficeTerm term, DateTimeOffset publishedAtUtc) =>
        HasEnded(term.End, publishedAtUtc) ? $"Former {Office(term)}" : Office(term);

    private static bool HasEnded(DateOnly? end, DateTimeOffset publishedAtUtc) => OfficeLede.HasEnded(end, DateOnly.FromDateTime(publishedAtUtc.UtcDateTime));

    public static string? Lede(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc)
    {
        if (LatestTerm(terms, publishedAtUtc) is not { } term) return null;
        return OfficeLede.Of(HasEnded(term.End, publishedAtUtc), term.Party, Role(term).ToLowerInvariant(), Place(term, OfficeLede.LedePlace), term.Caucus);
    }

    public static string? Tenure(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc)
    {
        if (Stretches(Started(terms, publishedAtUtc), (earlier, later) => earlier.Office == later.Office).LastOrDefault() is not { } stretch || ChamberOf(stretch.Latest.Office) is not { } chamber)
            return null;
        var since = MonthYear(stretch.Start);
        return HasEnded(stretch.End, publishedAtUtc)
            ? $"In {chamber} from {since} to {MonthYear(stretch.End!.Value)}."
            : $"In {chamber} since {since}.";
    }

    public static IReadOnlyList<PublishedOfficeStretch> OfficeStretches(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc) =>
        Stretches(Started(terms, publishedAtUtc), (earlier, later) => earlier.Office == later.Office && earlier.State == later.State
            && earlier.District == later.District && earlier.Party == later.Party);

    public static string OfficeStretchNote(IEnumerable<PublishedOfficeTerm> terms) => terms.All(term => ChamberOf(term.Office) is not null)
        ? $"{StretchMeaning} Source: congress-legislators."
        : StretchMeaning;

    private const string StretchMeaning = "Each row is one unbroken stretch in the same office.";

    private const int MaxSwearingInGapDays = 7;

    private static List<PublishedOfficeStretch> Stretches(IEnumerable<PublishedOfficeTerm> terms, Func<PublishedOfficeTerm, PublishedOfficeTerm, bool> sameSeat)
    {
        var stretches = new List<PublishedOfficeStretch>();
        foreach (var term in terms.OrderBy(term => term.Start))
        {
            if (stretches.Count > 0 && stretches[^1] is { } last && sameSeat(last.Latest, term) && Continues(last.End, term.Start))
                stretches[^1] = last with { Latest = term, End = term.End };
            else
                stretches.Add(new(term, term.Start, term.End));
        }
        return stretches;
    }

    private static bool Continues(DateOnly? end, DateOnly nextStart) => end is { } day && nextStart <= day.AddDays(MaxSwearingInGapDays);

    private static string? ChamberOf(string office) => office switch
    {
        MemberRosterParser.Senate => "the Senate",
        MemberRosterParser.House => "the House",
        _ => null
    };

    private static string MonthYear(DateOnly day) => day.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string? RecordsCountLine(ReleaseOfficialPage page)
    {
        var total = page.Records.Length + page.Votes.Length;
        if (total == 0) return null;
        var parts = page.Records.GroupBy(entry => entry.Kind).OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {KindNoun(group.Key, group.Count())}").ToList();
        if (!page.Votes.IsEmpty) parts.Add($"{Counted(page.Votes.Length, "vote")} in {TheCongress(page.Congress!.Value)}");
        return $"This list holds {Counted(total, "published record")}: {JoinAnd(parts)}. Rows are newest first.";
    }

    public static IReadOnlyList<FilterTab> CongressTabs(ReleaseOfficialPage page) =>
        [.. page.Congresses.Select((congress, index) => FilterTab.Link(
            CongressTabLabel(congress, index == 0),
            page.RootPath + (index == 0 ? PublicationProtocol.OfficialPagePath(page.Profile.Id) : PublicationProtocol.OfficialCongressPagePath(page.Profile.Id, congress)),
            selected: congress == page.Congress))];

    public static string CongressTabsLabel(ReleaseOfficialPage page) =>
        $"Show {TheCongress(page.Congresses[0])} or an earlier Congress";

    private static string CongressTabLabel(int congress, bool isCurrent)
    {
        var name = $"{CongressCalendar.Ordinal(congress)} Congress";
        if (!isCurrent) return $"{name} (votes only)";
        var years = $"{CongressCalendar.StartOf(congress).Year} to {CongressCalendar.StartOf(congress + 1).Year}";
        return $"All records, votes of the {name} ({years})";
    }

    public static string TheCongress(int congress) => $"the {CongressCalendar.Ordinal(congress)} Congress";

    private static string KindNoun(PublishedRecordKind kind, int count) =>
        (count == 1 ? PublishedRecordLabels.Kind(kind) : PublishedRecordLabels.KindPlural(kind)).ToLowerInvariant();

    public static string Counted(int count, string singular) => $"{count} {Noun(count, singular)}";

    private static string JoinAnd(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => $"{string.Join(", ", parts[..^1])}, and {parts[^1]}"
    };

    public static string PartyLine(string party, string? caucus) =>
        OfficeLede.CaucusWith(party, caucus) is { } with ? $"Party: {party}, {with}" : $"Party: {party}";

    public static PublishedOfficeTerm? LatestTerm(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc) =>
        Started(terms, publishedAtUtc).OrderBy(term => term.Start).LastOrDefault();

    private static IEnumerable<PublishedOfficeTerm> Started(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc) =>
        terms.Where(term => term.Start <= DateOnly.FromDateTime(publishedAtUtc.UtcDateTime));

    public static string OfficeKey(PublishedOfficeKind kind) => kind.ToString().ToLowerInvariant();

    public static readonly IReadOnlyList<(PublishedOfficeKind Kind, string Plural)> OfficeKinds =
    [
        (PublishedOfficeKind.Senator, "Senators"), (PublishedOfficeKind.Representative, "Representatives"), (PublishedOfficeKind.Executive, "Executives"),
        (PublishedOfficeKind.Judicial, "Justices"), (PublishedOfficeKind.Other, "Other officials")
    ];

    public static string RecordCount(int count) => $"{Counted(count, "record")} other than votes";

    public static IReadOnlyList<PublishedOfficialRow> Rows(ReleaseOfficialPage page)
    {
        var countKeys = page.TalkAndAction?.CountKeysByRecord();
        return [.. ReleaseRecordOrder.NewestFirst(page.Records.Select(entry => PublishedOfficialRow.Of(entry, page.BillStatuses?.GetValueOrDefault(entry.RecordId))).Concat(page.Votes.Select(PublishedOfficialRow.Of))
            .Select(row => row with { FilterKeys = countKeys is null ? null : string.Join(' ', countKeys[row.RecordId].Order(StringComparer.Ordinal)) }),
            row => row.Date, row => row.PublishedAtUtc, row => row.RecordId)];
    }

    public static string StatusLine(ActionBillStatus billStatus) =>
        $"Latest official action on {Moment.Day(billStatus.Status.Date)} (read from Congress.gov on {Moment.Day(billStatus.Status.ObservedAtUtc)}): {BillStatusMeaning.Plain(billStatus.Status.Text, billStatus.BillType)}";

    public const string AgainstPartyClass = "against-party";

    public static string RowClass(PublishedOfficialRow row) => row.AgainstParty ? $"{KindClass(row.Kind)} {AgainstPartyClass}" : KindClass(row.Kind);

    public static string KindClass(PublishedRecordKind kind) => $"kind-{PublicationProtocol.KindText(kind)}";

    public static IReadOnlyList<FilterTab> RecordTabs(IReadOnlyList<PublishedOfficialRow> rows, bool withProfile)
    {
        var kinds = rows.GroupBy(row => row.Kind).OrderBy(group => group.Key).ToList();
        var breaks = withProfile ? rows.Count(row => row.AgainstParty) : 0;
        var kindTabs = kinds.Count > 1 ? kinds : [];
        if (kindTabs.Count + breaks == 0 && !withProfile) return [];
        var tabs = new List<FilterTab> { FilterTab.Button("All", "", true, rows.Count) };
        tabs.AddRange(kindTabs.Select(group => FilterTab.Button(KindTabLabel(group.Key), KindClass(group.Key), false, group.Count())));
        if (breaks > 0) tabs.Add(FilterTab.Button("Against their party", AgainstPartyClass, false, breaks));
        if (withProfile) tabs.Add(FilterTab.Button(IssuesTabLabel, IssuesPane, false));
        return tabs;
    }

    public const string IssuesTabLabel = "Issues they work on";

    public const string IssuesPane = "issues";

    private static string KindTabLabel(PublishedRecordKind kind) => kind == PublishedRecordKind.Action ? "Bills" : PublishedRecordLabels.KindPlural(kind);

    public static string CastClass(string cast) => PublishedOfficialVote.SideOf(cast) == PublishedPartyMajority.Yea ? "cast-pill cast-yes" : "cast-pill";

    public const string ShowAllRecords = "Show all records";

    public const string NoTrackedIssue = "None of their statements, sponsored bills, or votes in this Congress is on a tracked issue.";

    public static string IssueNotice(string issueName, int congress) => $"Filtered to records on {issueName} in the {CongressCalendar.Ordinal(congress)} Congress";

    public const string StatementsSubject = "their statements";

    public const string BillsSubject = "bills they sponsored";

    public const string VotesSubject = "their substantive votes";

    public static string CountFilterLabel(string counted, string subject, string issueName) => $"{counted}: show {subject} on {issueName}";

    public static string CountFilterNotice(string subject, string issueName, int congress) =>
        $"Filtered to {subject} on {issueName} in the {CongressCalendar.Ordinal(congress)} Congress";

    public const string NoVoteOnRecord = "No vote on record";

    public const string NotAVote = "Not a vote";

    public const string NoStatementDate = "No date";

    public static string TheirVote(LinkedPair pair) => pair.TargetKind == PublishedRecordKind.Vote ? pair.Cast is { } cast ? PlainVoteWords.Cast(cast) : NoVoteOnRecord : NotAVote;

    public static string StatementDay(LinkedPair pair) => pair.StatementDay is { } day ? Moment.Day(day) : NoStatementDate;

    public static string MissedVotes(MissedVotes missed, int congress)
    {
        var share = missed.Share is not null ? $" ({Percent(missed.Missed, missed.Held)})" : "";
        var days = missed.First == missed.Last ? $"on {Moment.Day(missed.First)}" : $"{Moment.Day(missed.First)} to {Moment.Day(missed.Last)}";
        return $"Missed {missed.Missed} of the {missed.Held} roll call {Noun(missed.Held, "vote")} of {ChamberName(missed.Chamber)} on this site{share}, {days} ({CongressCalendar.Ordinal(congress)} Congress).";
    }

    public static string SplitVoteCount(int count, int cast)
    {
        var share = TalkAndActionProfile.ShareOf(count, cast) is not null ? $" ({Percent(count, cast)})" : "";
        return $"{count} of {cast} split {Noun(cast, "vote")}{share}";
    }

    public static string PartyOf(VotesWithParty votes) => PartyOf(votes.Chamber, votes.Party);

    public static string PartyOf(PublishedChamber chamber, string party) => PublishedPartyNames.PartyOf(chamber, party);

    private static string ChamberName(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "the Senate" : "the House of Representatives";

    public static string WebsiteHost(string website) => new Uri(website).IdnHost;

    public static string Noun(int count, string singular) => count == 1 ? singular : singular + "s";

    public static string Percent(int count, int of)
    {
        var percent = 100.0 * count / of;
        if (count > 0 && percent < 0.5) return "under 1%";
        if (count < of && percent >= 99.5) return "over 99%";
        return string.Create(CultureInfo.InvariantCulture, $"{percent:0}%");
    }

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"{PublishedVoteView.RollCall(vote.Chamber, vote.RollCallNumber)}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {BillNumbers.Of(vote.BillType, vote.BillNumber!.Value)}";
    }
}
