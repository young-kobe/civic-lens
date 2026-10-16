namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVote(HouseVoteRoll Roll, DateTimeOffset UpdateDate, string? BillType, int? BillNumber);
