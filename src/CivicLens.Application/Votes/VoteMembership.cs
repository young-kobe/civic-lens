namespace CivicLens.Application.Votes;

/// <summary>A tracked person who is a member of one vote record.</summary>
public sealed record VoteMembership(string RecordId, string PersonId);
