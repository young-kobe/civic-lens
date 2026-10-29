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
        term.End is { } end && end < DateOnly.FromDateTime(publishedAtUtc.UtcDateTime) ? $"Former {Office(term)}" : Office(term);

    public static string PartyLine(string party, string? caucus) =>
        caucus is null || caucus == party ? $"Party: {party}" : $"Party: {party}, caucuses with {Members(caucus)}";

    public static PublishedOfficeTerm? LatestTerm(IEnumerable<PublishedOfficeTerm> terms) => terms.OrderBy(term => term.Start).LastOrDefault();

    public static string OfficeKey(PublishedOfficeKind kind) => kind.ToString().ToLowerInvariant();

    public static readonly IReadOnlyList<(PublishedOfficeKind Kind, string Plural)> OfficeKinds =
    [
        (PublishedOfficeKind.Senator, "Senators"), (PublishedOfficeKind.Representative, "Representatives"), (PublishedOfficeKind.Executive, "Executives"),
        (PublishedOfficeKind.Judicial, "Justices"), (PublishedOfficeKind.Other, "Other officials")
    ];

    public static string RecordCount(int count) => $"{count} {Noun(count, "record")} other than votes";

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

    public static string CountFilterLabel(int count, string subject, string issueName) => $"{count}: show {subject} on {issueName}";

    public static string CountFilterNotice(string subject, string issueName, int congress, int count) =>
        $"Showing {subject} on {issueName} in the {CongressCalendar.Ordinal(congress)} Congress: {count} {Noun(count, "record")}";

    public const string LinkMeaning = "A reviewer approved each link. A link does not mean the vote or bill matches the statement.";

    public const string NoLinks = "No reviewed links between their statements and their bills or votes yet.";

    public const string NoVoteOnRecord = "No vote on record";

    public const string NotAVote = "Not a vote";

    public const string NoStatementDate = "No date";

    public static string TheirVote(LinkedPair pair) => pair.TargetKind == PublishedRecordKind.Vote ? pair.Cast ?? NoVoteOnRecord : NotAVote;

    public static string StatementDay(LinkedPair pair) => pair.StatementDay is { } day ? Moment.Iso(day) : NoStatementDate;

    public const string MissedVoteMeaning = "A missed vote means the official did not vote on it.";

    public static string MissedVotes(MissedVotes missed, int congress)
    {
        var share = missed.Share is { } value ? $" ({Percent(value)})" : "";
        var days = missed.First == missed.Last ? $"on {Moment.Day(missed.First)}" : $"{Moment.Day(missed.First)} to {Moment.Day(missed.Last)}";
        return $"Missed {missed.Missed} of {missed.Held} roll call {Noun(missed.Held, "vote")} of all kinds{share}, including procedural votes, " +
            $"held in {ChamberName(missed.Chamber)} while they served, {days} ({CongressCalendar.Ordinal(congress)} Congress).";
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

    public static string PartyOf(VotesWithParty votes) => $"{votes.Chamber} {Members(votes.Party)}";

    private static string Members(string party) => $"{party}s";

    private static string ChamberName(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "the Senate" : "the House of Representatives";

    private static string Noun(int count, string singular) => count == 1 ? singular : singular + "s";

    private static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%");

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"{vote.Chamber} roll call {vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value)}";
    }
}
