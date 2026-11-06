using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>One dated term of an official, as the release names it.</summary>
public sealed record OfficialTerm(string Office, PublishedOfficeKind Kind, string? State, int? District, string? Party, string? Caucus, DateOnly StartsOn, DateOnly? EndsOn);
