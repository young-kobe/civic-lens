namespace CivicLens.PublicApi.Maps;

public sealed record MapSeatAnswer(string State, int? District, int? SenateClass, string? OfficialId);
