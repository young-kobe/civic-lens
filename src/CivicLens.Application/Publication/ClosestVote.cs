using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The substantive vote with a party alignment, dated in the seven days that end on the release day, with the smallest gap between its Yea and Nay counts,
/// then the lowest record ID. An alignment comes only with counts.
/// </summary>
public sealed record ClosestVote(DateOnly WindowStart, DateOnly WindowEnd, PublishedRecordEntry Entry, int VotesInWindow)
{
    public string? BillLine { get; init; }

    public int Gap => GapOf(Entry);

    public static ClosestVote? From(PublicationRelease release)
    {
        var end = DateOnly.FromDateTime(release.PublishedAtUtc.UtcDateTime);
        var start = end.AddDays(1 - PublishedCountRules.ClosestVoteWindowDays);
        var votes = release.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote && entry.Alignment is not null)
            .Select(entry => (Entry: entry, Day: ReleaseRecordOrder.Day(entry))).Where(vote => vote.Day >= start && vote.Day <= end)
            .Select(vote => vote.Entry).ToList();
        var closest = votes.OrderBy(GapOf).ThenBy(vote => vote.RecordId, StringComparer.Ordinal).FirstOrDefault();
        return closest is null ? null : new(start, end, closest, votes.Count);
    }

    private static int GapOf(PublishedRecordEntry entry) => Math.Abs(entry.YeaCount!.Value - entry.NayCount!.Value);
}
