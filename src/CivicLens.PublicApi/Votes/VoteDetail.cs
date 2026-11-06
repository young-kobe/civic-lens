namespace CivicLens.PublicApi.Votes;

/// <summary><c>Facts</c> holds the fact texts of the vote record in position order.</summary>
public sealed record VoteDetail(int Release, VoteRow Vote, string[] IssueIds, string[] Facts);
