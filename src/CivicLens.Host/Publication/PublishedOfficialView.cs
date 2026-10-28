using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Host.Actions;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedOfficialView
{
    public static string Office(PublishedOfficeTerm term) => term.Office switch
    {
        "sen" => $"Senator for {term.State}",
        "rep" when term.District is { } district => $"Representative for {term.State}, district {district}",
        "rep" => $"Representative for {term.State}",
        "prez" => "President",
        "viceprez" => "Vice President",
        _ => term.Office
    };

    public static string WebsiteHost(string website) => new Uri(website).IdnHost;

    public static IReadOnlyList<PublishedOfficialRow> Rows(ReleaseOfficialPage page) =>
    [.. ReleaseRecordOrder.NewestFirst(page.Records.Select(PublishedOfficialRow.Of).Concat(page.Votes.Select(PublishedOfficialRow.Of)),
        row => row.Date, row => row.PublishedAtUtc, row => row.RecordId)];

    public static string Share(TalkAndActionColumn column, string issueId) => column.ShareOf(issueId) is { } share
        ? $"{column.CountOf(issueId)} ({Percent(share)})"
        : $"{column.CountOf(issueId)} (Too few records)";

    public static readonly IReadOnlyList<(string Value, string Text)> RecordFilters = [("all", "All records"), ("against-party", "Votes against their party")];

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

    public static string PartyOf(VotesWithParty votes) => $"{votes.Chamber} {votes.Party}s";

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
    DateTimeOffset PublishedAtUtc, bool AgainstParty)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry) => new(
        ReleaseRecordOrder.Day(entry), entry.RecordId, entry.Headline, entry.Kind, null, null, entry.FirstPublishedAtUtc, false);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordKind.Vote, $"{PublishedOfficialView.RollCall(vote)}. Result of the vote: {vote.Result}", vote.Cast, DateTimeOffset.MinValue, VotesWithParty.IsBreak(vote));
}
