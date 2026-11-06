using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The vote values that the stored columns keep: the party of each member, the alignment, and the majority of each major party.
/// Every value comes from the <see cref="PublishedVote"/> that the release writes, so the release files and the columns share one rule owner:
/// <see cref="VoteTally"/>, <see cref="VoteParties"/>, and <see cref="PublishedVote.AlignmentOfParties"/>. A column is null exactly where the release holds null.
/// The majority is null for a party with no member in the vote and for a tied party. The parties follow the order of <see cref="PublishedVote.Members"/>,
/// which is the order of the member list.
/// </summary>
public sealed record VoteStoredColumns(string RecordId, int RevisionNumber, PublishedVoteAlignment? Alignment, string? DemocratMajority,
    string? RepublicanMajority, ImmutableArray<string> MemberParties)
{
    public static VoteStoredColumns From(PublishedVote vote) => new(vote.RecordId, vote.RevisionNumber, vote.AlignmentOfParties(),
        vote.MajorityOf(PublishedPartyMajority.Democrat), vote.MajorityOf(PublishedPartyMajority.Republican), [.. vote.Members.Select(member => member.Party)]);
}
