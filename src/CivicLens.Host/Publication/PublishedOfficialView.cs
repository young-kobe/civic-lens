using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
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

    public static IReadOnlyList<PublishedOfficialRow> Rows(ReleaseOfficial official) =>
    [.. official.Records.Select(PublishedOfficialRow.Of).Concat(official.Votes.Select(PublishedOfficialRow.Of))
        .OrderByDescending(row => row.Date).ThenBy(row => row.RecordId, StringComparer.Ordinal)];

    public static string RollCall(PublishedOfficialVote vote)
    {
        var roll = $"Roll call {vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}";
        return vote.BillType is null ? roll : $"{roll}, {ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value)}";
    }
}

internal sealed record PublishedOfficialRow(DateOnly Date, string RecordId, string Headline, PublishedRecordKind Kind, string? Detail, string? Cast)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry) => new(
        entry.EventDate ?? DateOnly.FromDateTime(entry.FirstPublishedAtUtc.UtcDateTime), entry.RecordId, entry.Headline, entry.Kind, null, null);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordKind.Vote, PublishedOfficialView.RollCall(vote), vote.Cast);
}
