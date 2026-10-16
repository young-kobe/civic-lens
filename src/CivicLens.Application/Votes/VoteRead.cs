namespace CivicLens.Application.Votes;

public sealed record VoteRead(VoteSnapshot? Snapshot, string? ErrorCode);
