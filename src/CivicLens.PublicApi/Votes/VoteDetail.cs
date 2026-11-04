namespace CivicLens.PublicApi.Votes;

public sealed record VoteDetail(int Release, VoteRow Vote, string[] IssueIds);
