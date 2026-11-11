using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One tracked member on one roll call. The party is the group that the party counts use for the member.</summary>
public sealed record VoteMemberData(string OfficialId, string Name, string Cast, string? Party, PublishedCitation Citation);
