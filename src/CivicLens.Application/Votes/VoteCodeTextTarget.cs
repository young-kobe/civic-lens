namespace CivicLens.Application.Votes;

/// <summary>A published vote whose current revision holds model text, with the newest candidate of its roll call. An unchanged update
/// writes no revision, so that candidate can be newer than the candidate of the revision.</summary>
public sealed record VoteCodeTextTarget(string RecordId, string CandidateId);
