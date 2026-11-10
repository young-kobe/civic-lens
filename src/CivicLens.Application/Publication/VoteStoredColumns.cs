using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The vote values that storage keeps from the published revision: the party of each member, alignment, major-party majorities, and complete party tallies when available.
/// Every value comes from the <see cref="PublishedVote"/> that the release writes, so the release files and the columns share one rule owner:
/// <see cref="VoteTally"/>, <see cref="VoteParties"/>, and <see cref="PublishedVote.AlignmentOfParties"/>. A column is null exactly where the release holds null.
/// The majority is null for a party with no member in the vote and for a tied party. The parties follow the order of <see cref="PublishedVote.Members"/>,
/// which is the order of the member list. The parties pin the member list when it holds none. An empty array pins nothing, so a revision can write its own
/// columns without it changing the parties of a list that another revision owns.
/// </summary>
public sealed record VoteStoredColumns(string RecordId, int RevisionNumber, PublishedVoteAlignment? Alignment, string? DemocratMajority,
    string? RepublicanMajority, ImmutableArray<string> MemberParties)
{
    /// <summary>Default means the exact published vote does not have complete tallies. A nondefault array retains its complete party groups and counts.</summary>
    public ImmutableArray<PublishedPartyTally> PartyTallies { get; init; }

    public static VoteStoredColumns From(PublishedVote vote) => new(vote.RecordId, vote.RevisionNumber, vote.AlignmentOfParties(),
        vote.MajorityOf(PublishedPartyMajority.Democrat), vote.MajorityOf(PublishedPartyMajority.Republican), [.. vote.Members.Select(member => member.Party)])
    {
        PartyTallies = vote.PartyTallies is null ? default : [.. vote.PartyTallies]
    };
}
