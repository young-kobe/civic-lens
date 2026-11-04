using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The substantive vote with a party alignment, dated in the seven days that end on the release day, with the smallest gap between its Yea and Nay counts,
/// then the lowest record ID. An alignment comes only with counts.
/// </summary>
public sealed record ClosestVote(DateOnly WindowStart, DateOnly WindowEnd, PublishedRecordEntry Entry, int VotesInWindow)
{
    public const int WindowDays = 7;

    public int Gap => Math.Abs(Entry.YeaCount!.Value - Entry.NayCount!.Value);

    public static ClosestVote? From(PublicationRelease release)
    {
        var end = DateOnly.FromDateTime(release.PublishedAtUtc.UtcDateTime);
        var start = end.AddDays(1 - WindowDays);
        var votes = release.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote && entry.Alignment is not null &&
            ReleaseRecordOrder.Day(entry) >= start && ReleaseRecordOrder.Day(entry) <= end).ToList();
        var closest = votes.OrderBy(vote => Math.Abs(vote.YeaCount!.Value - vote.NayCount!.Value)).ThenBy(vote => vote.RecordId, StringComparer.Ordinal).FirstOrDefault();
        return closest is null ? null : new(start, end, closest, votes.Count);
    }
}
