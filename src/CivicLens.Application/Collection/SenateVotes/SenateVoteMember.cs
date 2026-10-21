namespace CivicLens.Application.Collection.SenateVotes;

public sealed record SenateVoteMember(string LisId, string? FirstName, string LastName, string Party, string State, string VoteCast);
