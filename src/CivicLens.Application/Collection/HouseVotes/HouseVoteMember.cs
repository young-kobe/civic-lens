namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVoteMember(string BioguideId, string? FirstName, string? LastName, string? Party, string? State,
    string VoteCast);
