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
    [.. votes.Where(HasMajorParty)
        .GroupBy(vote => (vote.Chamber, vote.Party)).OrderBy(group => group.Key.Chamber).ThenBy(group => group.Key.Party, StringComparer.Ordinal)
        .Select(group => From(group.Key.Chamber, group.Key.Party, group.Where(vote => vote.SplitVote).ToArray()))];

    public static bool IsBreak(PublishedOfficialVote vote) => vote.SplitVote && IsAgainstPartyMajority(vote.Party, vote.Cast, vote.PartyMajority);

    public static bool IsAgainstPartyMajority(string party, string cast, string? partyMajority) =>
        IsMajorParty(party) && partyMajority is not null && PublishedOfficialVote.SideOf(cast) is { } side && side != partyMajority;

    private static bool HasMajorParty(PublishedOfficialVote vote) => IsMajorParty(vote.Party);

    private static bool IsMajorParty(string party) => party is PublishedPartyMajority.Democrat or PublishedPartyMajority.Republican;

    private static VotesWithParty From(PublishedChamber chamber, string party, PublishedOfficialVote[] splitVotes)
    {
        var breaks = splitVotes.Where(IsBreak).OrderByDescending(vote => vote.VotedOn).ThenByDescending(vote => vote.Session)
            .ThenByDescending(vote => vote.RollCallNumber).ToImmutableArray();
        var matched = splitVotes.Count(vote => PublishedOfficialVote.SideOf(vote.Cast) is not null) - breaks.Length;
        return new(chamber, party, matched, breaks, splitVotes.Length - matched - breaks.Length);
    }
}
