using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.SenateVotes;

public sealed record SenateVoteMenu(VoteSession Session, IReadOnlyList<SenateVoteMenuItem> Votes);
