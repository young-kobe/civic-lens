using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Host.Actions;
using CivicLens.Core.Relationships;
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

    public const string MissedVoteMeaning = "A missed vote means the official did not vote on it.";

    public static string MissedVotes(MissedVotes missed, int congress)
    {
        var chamber = missed.Chamber == PublishedChamber.Senate ? "the Senate" : "the House of Representatives";
        var share = missed.Share is { } value ? $" ({Percent(value)})" : "";
        return $"Missed {missed.Missed} of {missed.Held} roll call votes{share} held in {chamber} while they served, " +
            $"{Moment.Day(missed.First)} to {Moment.Day(missed.Last)} ({CongressCalendar.Ordinal(congress)} Congress).";
    }

    private static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%");

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"{vote.Chamber} roll call {vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value)}";
    }
}

internal sealed record PublishedOfficialRow(DateOnly Date, string RecordId, string Headline, PublishedRecordKind Kind, string? Detail, string? Cast,
    DateTimeOffset PublishedAtUtc)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry) => new(
        ReleaseRecordOrder.Day(entry), entry.RecordId, entry.Headline, entry.Kind, null, null, entry.FirstPublishedAtUtc);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordKind.Vote, $"{PublishedOfficialView.RollCall(vote)}. Result of the vote: {vote.Result}", vote.Cast, DateTimeOffset.MinValue);
}
