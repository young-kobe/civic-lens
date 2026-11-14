using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the summary rows of the officials from their vote rows. The vote figures of an untouched
/// official come from the previous release. The cosponsor count comes from the manifest entries of every publish, so it never goes stale. It is null while any action entry has no cosponsor data.
/// </summary>
internal static class ReleaseOfficialSummaries
{
    public static PublishedOfficialSummary Of(string officialId, PublishedVoteFigures current, PublishedVoteFigures? previous) => new()
    {
        Id = officialId,
        Congress = current.Congress,
        CosponsoredBills = null,
        PartyVotes = current.PartyVotes,
        Attendance = current.Attendance,
        Previous = previous
    };

    public static PublishedVoteFigures FiguresOf(int congress, IEnumerable<PublishedOfficialVote> votes)
    {
        var rows = votes.ToArray();
        return new()
        {
            Congress = congress,
            PartyVotes = PartyVotesOf(VotesWithParty.Of(rows)),
            Attendance = AttendanceOf(MissedVotes.Of(rows))
        };
    }

    private static PublishedPartyVoteSummary[] PartyVotesOf(IEnumerable<VotesWithParty> parties) =>
        [.. parties.Select(party => new PublishedPartyVoteSummary
        {
            Chamber = party.Chamber,
            Party = party.Party,
            SplitVotesCast = party.Cast,
            Breaks = party.Breaks.Length
        })];

    private static PublishedAttendanceSummary[] AttendanceOf(IEnumerable<MissedVotes> missed) =>
        [.. missed.Select(row => new PublishedAttendanceSummary
        {
            Chamber = row.Chamber,
            Held = row.Held,
            Missed = row.Missed,
            First = row.First,
            Last = row.Last
        })];

    public static Dictionary<string, int>? CosponsoredCounts(IEnumerable<PublishedRecordEntry> entries)
    {
        var actions = entries.Where(entry => entry.Kind == PublishedRecordKind.Action).ToList();
        if (actions.Any(action => action.CosponsorIds is null)) return null;
        return actions.SelectMany(action => action.CosponsorIds!).GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public static Task WriteAsync(IReleaseStaging staging, IReadOnlyList<PublishedOfficialSummary> rows, CancellationToken cancellationToken)
    {
        var file = new PublishedOfficialSummaries { Officials = [.. rows] };
        file.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions);
        if (json.Length > PublishedOfficialSummaries.MaximumBytes)
            throw new ArgumentException("The official summaries file would exceed its size limit.", nameof(rows));
        return staging.WriteFileAsync(PublicationProtocol.OfficialSummariesPath, json, cancellationToken);
    }
}
