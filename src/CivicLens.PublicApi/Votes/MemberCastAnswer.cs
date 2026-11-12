namespace CivicLens.PublicApi.Votes;

/// <summary>The casts of the asked officials in the active release, by the record ID of the vote.</summary>
public sealed record MemberCastAnswer(int Release, Dictionary<string, SharedMemberVoteCast[]> ByVote);
