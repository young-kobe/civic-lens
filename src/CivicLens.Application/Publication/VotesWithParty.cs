using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// How often one official voted with the party majority of the official on split votes in one chamber.
/// A split vote is a substantive one where most Democrats and most Republicans voted on opposite sides. Only a Yea or Nay cast counts.
/// NotCast counts the split votes with another cast, such as Present or Not Voting. Breaks are the casts against the party majority, newest first.
/// </summary>
public sealed record VotesWithParty(PublishedChamber Chamber, string Party, int Matched, ImmutableArray<PublishedOfficialVote> Breaks, int NotCast)
{
    public int Cast => Matched + Breaks.Length;

    public double? Share => TalkAndActionProfile.ShareOf(Matched, Cast);

    public static ImmutableArray<VotesWithParty> Of(IEnumerable<PublishedOfficialVote> votes) =>
    [.. votes.Where(vote => vote.Party is PublishedPartyMajority.Democrat or PublishedPartyMajority.Republican)
        .GroupBy(vote => (vote.Chamber, vote.Party)).OrderBy(group => group.Key.Chamber).ThenBy(group => group.Key.Party, StringComparer.Ordinal)
        .Select(group => From(group.Key.Chamber, group.Key.Party, group.Where(vote => vote.SplitVote).ToArray()))];

    private static VotesWithParty From(PublishedChamber chamber, string party, PublishedOfficialVote[] splitVotes)
    {
        var sided = splitVotes.Select(vote => (Vote: vote, Side: PublishedOfficialVote.SideOf(vote.Cast))).Where(cast => cast.Side is not null)
            .ToLookup(cast => cast.Side == cast.Vote.PartyMajority, cast => cast.Vote);
        var breaks = sided[false].OrderByDescending(vote => vote.VotedOn).ThenByDescending(vote => vote.Session)
            .ThenByDescending(vote => vote.RollCallNumber).ToImmutableArray();
        return new(chamber, party, sided[true].Count(), breaks, splitVotes.Length - sided[true].Count() - breaks.Length);
    }
}
