namespace CivicLens.Application.Actions;

/// <summary>A published bill record whose current revision holds retired text, with the candidate of that revision.</summary>
public sealed record ActionCodeTextTarget(string RecordId, string CandidateId);
