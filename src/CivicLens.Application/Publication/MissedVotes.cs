using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The roll calls that one official missed in one chamber. A roll call is missed when the official did not vote on it.
/// Held counts the published roll calls of the chamber while the official served.
/// </summary>
public sealed record MissedVotes(PublishedChamber Chamber, int Missed, int Held, DateOnly First, DateOnly Last)
{
    public double? Share => TalkAndActionProfile.ShareOf(Missed, Held);

    public static ImmutableArray<MissedVotes> Of(IEnumerable<PublishedOfficialVote> votes) =>
    [.. votes.GroupBy(vote => vote.Chamber).OrderBy(chamber => chamber.Key).Select(chamber => new MissedVotes(chamber.Key,
        chamber.Count(vote => !vote.IsCast), chamber.Count(), chamber.Min(vote => vote.VotedOn), chamber.Max(vote => vote.VotedOn)))];
}
