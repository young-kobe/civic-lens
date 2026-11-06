namespace CivicLens.PublicApi.Votes;

/// <summary>The newest substantive votes of each asked official. The release lets a caller compare this answer with another answer, such as the ZIP answer.
/// <c>Officials</c> follows the order of the request and holds one entry for each asked official, also an official with no vote.</summary>
public sealed record MemberVoteAnswer(int Release, OfficialVotes[] Officials);
