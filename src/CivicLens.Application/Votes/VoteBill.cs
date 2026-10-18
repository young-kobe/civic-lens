namespace CivicLens.Application.Votes;

public sealed record VoteBill(string VoteRecordId, int Congress, string BillType, int BillNumber);
