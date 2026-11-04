namespace CivicLens.PublicApi.Votes;

public sealed record VotePage(int Release, VoteRow[] Votes, string? NextCursor);
