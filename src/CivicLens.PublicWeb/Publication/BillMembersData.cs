using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>How the current members of one ZIP area relate to one bill, from one release. <c>VotedChambers</c> are the chambers that hold a roll call vote on the bill in that release.</summary>
public sealed record BillMembersData(string Zcta, string RecordId, int Release, BillMemberData[] Members, PublishedChamber[] VotedChambers);

public sealed record BillMemberData(string Name, PublishedChamber Chamber, BillMemberRelation Relation, DateOnly? JoinedOn, BillMemberCastData[] Casts);

public sealed record BillMemberCastData(DateOnly VotedOn, string CastWords, string? Side);

/// <summary>Sponsor, cosponsor that did not withdraw, or neither.</summary>
public enum BillMemberRelation { None, Sponsor, Cosponsor }
