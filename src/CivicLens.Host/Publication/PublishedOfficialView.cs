using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Relationships;
using CivicLens.Host.Actions;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedOfficialView
{
    public static string Office(PublishedOfficeTerm term) => term.Office switch
    {
        MemberRosterParser.Senate => $"Senator for {term.State}",
        MemberRosterParser.House when term.District is { } district => $"Representative for {term.State}, district {district}",
        MemberRosterParser.House => $"Representative for {term.State}",
        ExecutiveRosterParser.President => "President",
        ExecutiveRosterParser.VicePresident => "Vice President",
        _ => term.Office
    };

    public static string CardOffice(PublishedOfficeTerm term, DateTimeOffset publishedAtUtc) =>
        HasEnded(term.End, publishedAtUtc) ? $"Former {Office(term)}" : Office(term);

    private static bool HasEnded(DateOnly? end, DateTimeOffset publishedAtUtc) => end is { } day && day < DateOnly.FromDateTime(publishedAtUtc.UtcDateTime);

    public static string? Lede(IEnumerable<PublishedOfficeTerm> terms, DateTimeOffset publishedAtUtc)
    {
        if (LatestTerm(terms, publishedAtUtc) is not { } term) return null;
        var sentences = new[]
        {
            $"{CardOffice(term, publishedAtUtc)}.",
            term.Party is { } party ? $"{PartyLine(party, term.Caucus)}." : null,
            Tenure(terms, publishedAtUtc)
        };
        return string.Join(' ', sentences.OfType<string>());
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
            .Select(group => Counted(group.Count(), PublishedRecordLabels.Kind(group.Key).ToLowerInvariant())).ToList();
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

    public static string Counted(int count, string singular) => $"{count} {Noun(count, singular)}";

    private static string JoinAnd(List<string> parts) => parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";

    public static string PartyLine(string party, string? caucus) =>
        caucus is null || caucus == party ? $"Party: {party}" : $"Party: {party}, caucuses with {Members(caucus)}";

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

    public static string WebsiteHost(string website) => new Uri(website).IdnHost;

    public static IReadOnlyList<PublishedOfficialRow> Rows(ReleaseOfficialPage page)
    {
        var countKeys = page.TalkAndAction?.CountKeysByRecord();
        return [.. ReleaseRecordOrder.NewestFirst(page.Records.Select(PublishedOfficialRow.Of).Concat(page.Votes.Select(PublishedOfficialRow.Of))
            .Select(row => row with { FilterKeys = countKeys is null ? null : string.Join(' ', countKeys[row.RecordId].Order(StringComparer.Ordinal)) }),
            row => row.Date, row => row.PublishedAtUtc, row => row.RecordId)];
    }

    public static readonly IReadOnlyList<(string Value, string Text)> RecordFilters = [("all", "All records"), ("against-party", "Votes against their party")];

    public const string ShowAllRecords = "Show all records";

    public const string StatementsSubject = "their statements";

    public const string BillsSubject = "bills they sponsored";

    public const string VotesSubject = "their substantive votes";

    public static string CountFilterLabel(string counted, string subject, string issueName) => $"{counted}: show {subject} on {issueName}";

    public static string CountFilterNotice(string subject, string issueName, int congress, int count) =>
        $"Showing {subject} on {issueName} in the {CongressCalendar.Ordinal(congress)} Congress: {count} {Noun(count, "record")}";

    public const string LinkMeaning = "A reviewer approved each link. A link does not mean the vote or bill matches the statement.";

    public const string NoLinks = "No reviewed links between their statements and their bills or votes yet.";

    public const string NoVoteOnRecord = "No vote on record";

    public const string NotAVote = "Not a vote";

    public const string NoStatementDate = "No date";

    public static string TheirVote(LinkedPair pair) => pair.TargetKind == PublishedRecordKind.Vote ? pair.Cast ?? NoVoteOnRecord : NotAVote;

    public static string StatementDay(LinkedPair pair) => pair.StatementDay is { } day ? Moment.Iso(day) : NoStatementDate;

    public const string MissedVoteMeaning = "A missed vote means the official did not vote on it. The count includes procedural votes. It counts only the roll calls on this site, which can be fewer than the chamber held.";

    public static string MissedVotes(MissedVotes missed, int congress)
    {
        var share = missed.Share is { } value ? $" ({Percent(value)})" : "";
        var days = missed.First == missed.Last ? $"on {Moment.Day(missed.First)}" : $"{Moment.Day(missed.First)} to {Moment.Day(missed.Last)}";
        return $"Missed {missed.Missed} of the {missed.Held} roll call {Noun(missed.Held, "vote")} of {ChamberName(missed.Chamber)} on this site{share}, {days} ({CongressCalendar.Ordinal(congress)} Congress).";
    }

    public static readonly string SplitVoteMeaning = $"A split vote is a substantive roll call vote where most {PublishedPartyMajority.Democrat}s and most {PublishedPartyMajority.Republican}s voted on opposite sides.";

    public static string WithParty(VotesWithParty votes, int congress)
    {
        var share = votes.Share is { } value ? $" ({Percent(value)})" : "";
        var inCongress = $"in the {CongressCalendar.Ordinal(congress)} Congress";
        var sentence = votes.Cast == 0
            ? $"No Yea or Nay cast yet on a split vote in {ChamberName(votes.Chamber)} as a {votes.Party} {inCongress}."
            : $"Voted with most {PartyOf(votes)} on {votes.Matched} of {votes.Cast} split {Noun(votes.Cast, "vote")}{share} {inCongress}.";
        var other = votes.Cast == 0 ? "" : "other ";
        return votes.NotCast == 0 ? sentence : $"{sentence} They did not cast Yea or Nay on {votes.NotCast} {other}split {Noun(votes.NotCast, "vote")}.";
    }

    public static string PartyOf(VotesWithParty votes) => PartyOf(votes.Chamber, votes.Party);

    public static string PartyOf(PublishedChamber chamber, string party) => $"{chamber} {Members(party)}";

    public static string PartyMemberOf(PublishedChamber chamber, string party) => $"{chamber} {party}";

    private static string Members(string party) => $"{party}s";

    private static string ChamberName(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "the Senate" : "the House of Representatives";

    public static string Noun(int count, string singular) => count == 1 ? singular : singular + "s";

    private static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%");

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"{vote.Chamber} roll call {vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value)}";
    }
}
