namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVoteMemberVotes(HouseVote Vote, IReadOnlyList<HouseVoteMember> Members);
