namespace CivicLens.Application.Collection.SenateVotes;

public sealed record SenateVoteDocument(string Type, int Number, string Name, string? Title);
