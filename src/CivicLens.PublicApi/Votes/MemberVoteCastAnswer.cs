using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>The recorded casts of the asked officials on one vote in the active release. A null chamber means that the release holds no such vote.</summary>
public sealed record MemberVoteCastAnswer(int Release, PublishedChamber? Chamber, MemberVoteCast[] Casts);
