namespace CivicLens.PublicApi.Votes;

/// <summary>An actual recorded cast by one asked official.</summary>
public sealed record SharedMemberVoteCast(string OfficialId, string Cast, string CastWords, string? Side, string? PartyMajorityCast);
