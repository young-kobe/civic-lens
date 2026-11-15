using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// How the current members of one ZIP area voted on one roll call, from one release. <c>Members</c> are the members of the chamber of the vote.
/// <c>OtherChamberMembers</c> counts the members of the other chamber, who do not vote on it. <c>VacantSeats</c> is true when the ZIP area has a seat in the chamber of the vote and no member holds any of them.
/// </summary>
public sealed record VoteMembersData(string Zcta, string RecordId, int Release, PublishedChamber Chamber, VoteZipMemberData[] Members, int OtherChamberMembers, bool VacantSeats);

/// <summary>One ZIP member in the chamber of the vote. A null cast means that the member has no recorded position on the vote. <c>Party</c> is the party group of the member on the vote.
/// <c>BreaksParty</c> is <c>public_api.member_votes.breaks_party</c>: true for a cast against most of the party, false for a cast with it, and null when the vote gives no party line.</summary>
public sealed record VoteZipMemberData(string OfficialId, string Name, string SeatTitle, string? Cast, string? Party, bool? BreaksParty);
