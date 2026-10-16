namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVotePartyTotal(string Party, int? Yea, int? Nay, int? Present, int? NotVoting);
