using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The member at the top of the first board list of one chamber and party: the highest share of split votes cast against most of the party. A board owns the ranking, the Congress, and the minimum of split votes. Only a member with at least one break can lead.</summary>
public sealed record BreakLeader(PublishedChamber Chamber, string Party, OfficialListing Official, int Cast, int Breaks)
{
    public static ImmutableArray<BreakLeader> All(IEnumerable<OfficialBoardPage> boards) =>
        [.. boards.Select(Of).OfType<BreakLeader>()];

    private static BreakLeader? Of(OfficialBoardPage page) =>
        page.Breakers.FirstOrDefault() is { Breaks: > 0 } leader
            ? new(page.Board.Chamber, page.Board.Party, leader.Official, leader.Cast, leader.Breaks)
            : null;
}
