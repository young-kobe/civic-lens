using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Host.Actions;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedOfficialView
{
    private const string Senate = "sen";
    private const string House = "rep";
    private const string President = "prez";
    private const string VicePresident = "viceprez";

    public static string Office(PublishedOfficeTerm term) => term.Office switch
    {
        Senate => $"Senator for {term.State}",
        House when term.District is { } district => $"Representative for {term.State}, district {district}",
        House => $"Representative for {term.State}",
        President => "President",
        VicePresident => "Vice President",
        _ => term.Office
    };

    public static string CardOffice(PublishedOfficeTerm term) =>
        term.End is { } end && end < DateOnly.FromDateTime(DateTime.UtcNow) ? $"Former {Office(term)}" : Office(term);

    public static string PartyLine(string party, string? caucus) =>
        caucus is null || caucus == party ? $"Party: {party}" : $"Party: {party}, caucuses with {Members(caucus)}";

    public static PublishedOfficeTerm? LatestTerm(IEnumerable<PublishedOfficeTerm> terms) => terms.OrderBy(term => term.Start).LastOrDefault();

    public static string OfficeKind(PublishedOfficeTerm? term) => term?.Office switch
    {
        Senate => "senator",
        House => "representative",
        President or VicePresident => "executive",
        _ => "other"
    };

    public static readonly IReadOnlyList<(string Kind, string Plural)> OfficeKinds =
        [("senator", "Senators"), ("representative", "Representatives"), ("executive", "Executives"), ("other", "Other officials")];

    public static string RecordCount(int count) => $"{count} {(count == 1 ? "record" : "records")} other than votes";

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
        $"Showing {subject} on {issueName} in the {CongressCalendar.Ordinal(congress)} Congress: {count} {(count == 1 ? "record" : "records")}";

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
        return $"Missed {missed.Missed} of {missed.Held} roll call {Votes(missed.Held)} of all kinds{share}, including procedural votes, " +
            $"held in {ChamberName(missed.Chamber)} while they served, {days} ({CongressCalendar.Ordinal(congress)} Congress).";
    }

    public static readonly string SplitVoteMeaning = $"A split vote is a substantive roll call vote where most {PublishedPartyMajority.Democrat}s and most {PublishedPartyMajority.Republican}s voted on opposite sides.";

    public static string WithParty(VotesWithParty votes, int congress)
    {
        var share = votes.Share is { } value ? $" ({Percent(value)})" : "";
        var inCongress = $"in the {CongressCalendar.Ordinal(congress)} Congress";
        var sentence = votes.Cast == 0
            ? $"No Yea or Nay cast yet on a split vote in {ChamberName(votes.Chamber)} as a {votes.Party} {inCongress}."
            : $"Voted with most {PartyOf(votes)} on {votes.Matched} of {votes.Cast} split {Votes(votes.Cast)}{share} {inCongress}.";
        var other = votes.Cast == 0 ? "" : "other ";
        return votes.NotCast == 0 ? sentence : $"{sentence} They did not cast Yea or Nay on {votes.NotCast} {other}split {Votes(votes.NotCast)}.";
    }

    public static string PartyOf(VotesWithParty votes) => $"{votes.Chamber} {Members(votes.Party)}";

    private static string Members(string party) => $"{party}s";

    private static string ChamberName(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "the Senate" : "the House of Representatives";

    private static string Votes(int count) => count == 1 ? "vote" : "votes";

    private static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%");

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"{vote.Chamber} roll call {vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value)}";
    }
}

internal sealed record PublishedOfficialRow(DateOnly Date, string RecordId, string Headline, PublishedRecordKind Kind, string? Detail, string? Cast,
    DateTimeOffset PublishedAtUtc, bool AgainstParty, string? FilterKeys = null)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry) => new(
        ReleaseRecordOrder.Day(entry), entry.RecordId, entry.Headline, entry.Kind, null, null, entry.FirstPublishedAtUtc, false);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordKind.Vote, $"{PublishedOfficialView.RollCall(vote)}. Result of the vote: {vote.Result}", vote.Cast, DateTimeOffset.MinValue, VotesWithParty.IsBreak(vote));
}
