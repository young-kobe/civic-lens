using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.HouseVotes;

public readonly record struct HouseVoteRoll(VoteSession Session, int Number);
