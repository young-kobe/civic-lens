namespace CivicLens.Application.Actions;

/// <summary>A published bill record whose current revision holds retired text, with the newest candidate of its bill. An
/// unchanged update writes no revision, so that candidate can be newer than the candidate of the revision.</summary>
public sealed record ActionCodeTextTarget(string RecordId, string CandidateId);
