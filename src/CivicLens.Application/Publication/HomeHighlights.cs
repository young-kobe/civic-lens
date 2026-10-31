using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The facts that the home page cards show, picked by fixed rules from the release. The closest vote is the vote with counts, dated in the
/// seven days that end on the release day, with the smallest gap between its Yea and Nay counts, then the lowest record ID.
/// </summary>
public sealed record HomeHighlights(DateOnly WindowStart, DateOnly WindowEnd, PublishedRecordEntry? ClosestVote, int VotesInWindow,
    ImmutableArray<PublishedRecordEntry> ExecutiveActions, int ExecutiveActionTotal, ImmutableArray<BreakLeader> BreakLeaders)
{
    public const int WindowDays = 7;
    public const int ExecutiveActionCount = 3;

    public static HomeHighlights From(PublicationRelease release, IReadOnlyList<OfficialListing> officials,
        IReadOnlyList<PublishedOfficialSummary> summaries)
    {
        var end = DateOnly.FromDateTime(release.PublishedAtUtc.UtcDateTime);
        var start = end.AddDays(1 - WindowDays);
        var votes = release.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote && HasCounts(entry) &&
            ReleaseRecordOrder.Day(entry) >= start && ReleaseRecordOrder.Day(entry) <= end).ToList();
        var executiveActions = ReleaseRecordOrder.NewestFirst(release.Records.Where(entry => entry.Kind == PublishedRecordKind.ExecutiveAction),
            ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId).ToList();
        return new(start, end, votes.OrderBy(Gap).ThenBy(entry => entry.RecordId, StringComparer.Ordinal).FirstOrDefault(), votes.Count,
            [.. executiveActions.Take(ExecutiveActionCount)], executiveActions.Count, BreakLeader.All(officials, summaries));
    }

    public static int Gap(PublishedRecordEntry vote) => Math.Abs(vote.YeaCount!.Value - vote.NayCount!.Value);

    private static bool HasCounts(PublishedRecordEntry entry) => entry.YeaCount + entry.NayCount > 0;
}
