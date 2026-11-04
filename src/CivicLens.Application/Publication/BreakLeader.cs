using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The member at the top of the first board list of one chamber and party: the highest share of split votes cast against most of the party. A board owns the ranking, the Congress, and the minimum of split votes. Only a member with at least one break can lead. The leader keeps the Congress and the page of the board. <see cref="All"/> orders the leaders by that share, then by more breaks, then by official ID.</summary>
public sealed record BreakLeader(PublishedChamber Chamber, string Party, BoardMember Member, int Congress)
{
    public static ImmutableArray<BreakLeader> All(IEnumerable<OfficialBoardPage> boards) =>
        [.. boards.Select(Of).OfType<BreakLeader>()
            .OrderByDescending(leader => leader.Member.BreakShare).ThenByDescending(leader => leader.Member.Breaks)
            .ThenBy(leader => leader.Member.Official.Profile.Id, StringComparer.Ordinal)];

    private static BreakLeader? Of(OfficialBoardPage page) =>
        page.Breakers.FirstOrDefault() is { Breaks: > 0 } leader ? new(page.Board.Chamber, page.Board.Party, leader, page.Congress!.Value) : null;
}
