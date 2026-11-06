namespace CivicLens.PublicApi.Votes;

/// <summary>The newest substantive votes of one official, newest first. The release lets a caller compare this answer with another answer, such as the ZIP answer.
/// A vote is listed only when the official has a cast on it. An official with no cast gets an empty list.</summary>
public sealed record MemberVoteAnswer(int Release, string OfficialId, MemberVote[] Votes);
