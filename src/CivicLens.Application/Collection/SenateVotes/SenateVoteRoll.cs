using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.SenateVotes;

public readonly record struct SenateVoteRoll(VoteSession Session, int Number);
