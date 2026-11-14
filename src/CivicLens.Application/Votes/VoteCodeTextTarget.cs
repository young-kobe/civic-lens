namespace CivicLens.Application.Votes;

/// <summary>A published vote whose current revision holds model text, with the candidate of that revision.</summary>
public sealed record VoteCodeTextTarget(string RecordId, string CandidateId);
