namespace CivicLens.Application.Collection.SenateVotes;

public sealed record SenateVote(SenateVoteRoll Roll, DateTimeOffset ModifyDate, string? BillType, int? BillNumber);
