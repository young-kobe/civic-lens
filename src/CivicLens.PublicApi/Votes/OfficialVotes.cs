namespace CivicLens.PublicApi.Votes;

/// <summary>The newest votes of one official, newest first, at most <see cref="MemberVoteStore.MaximumVotes"/>. A vote is listed only when the official has a cast on it.
/// An official with no cast gets an empty list.</summary>
public sealed record OfficialVotes(string OfficialId, MemberVote[] Votes);
