namespace CivicLens.PublicApi.Votes;

public sealed record VoteMember(int Position, string OfficialId, string Cast, string? Party, string? Caucus);
