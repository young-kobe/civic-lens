using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

public sealed record VoteMember(int Position, string OfficialId, string Cast, string? Party, string? Caucus, string? State, int? District, PublishedCitation Citation);
