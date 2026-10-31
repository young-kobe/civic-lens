using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the summary rows of the officials from the figures that the official pages already compute. The vote figures of an untouched
/// official come from the previous release. The cosponsor count comes from the manifest entries of every publish, so it never goes stale. It is null while any action entry has no cosponsor data.
/// </summary>
internal static class ReleaseOfficialSummaries
{
    public static PublishedOfficialSummary Of(string officialId, int? congress, TalkAndActionProfile? profile) =>
        profile is null
            ? PublishedOfficialSummary.Empty(officialId)
            : new()
            {
                Id = officialId,
                Congress = congress,
                CosponsoredBills = null,
                PartyVotes = [.. profile.WithPartyByChamber.Select(party => new PublishedPartyVoteSummary
                {
                    Chamber = party.Chamber,
                    Party = party.Party,
                    SplitVotesCast = party.Cast,
                    Breaks = party.Breaks.Length
                })],
                Attendance = [.. profile.MissedByChamber.Select(missed => new PublishedAttendanceSummary
                {
                    Chamber = missed.Chamber,
                    Held = missed.Held,
                    Missed = missed.Missed,
                    First = missed.First,
                    Last = missed.Last
                })]
            };

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
