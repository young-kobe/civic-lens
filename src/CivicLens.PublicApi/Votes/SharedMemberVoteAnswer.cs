namespace CivicLens.PublicApi.Votes;

/// <summary>The bounded shared vote list and the officials the caller asked about, in request order.</summary>
public sealed record SharedMemberVoteAnswer(int Release, string[] OfficialIds, SharedMemberVote[] Votes);
