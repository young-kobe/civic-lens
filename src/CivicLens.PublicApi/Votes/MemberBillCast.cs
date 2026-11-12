using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>One recorded cast. <c>CastWords</c> is the plain word for the cast, and <c>Side</c> is the cast side that the shared cast badge shows.</summary>
public sealed record MemberBillCast(string OfficialId, PublishedChamber Chamber, DateOnly VotedOn, string CastWords, string? Side);
