namespace CivicLens.PublicApi.Votes;

public sealed record VoteMemberPage(int Release, VoteMember[] Members, string? NextCursor);
