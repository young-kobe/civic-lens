namespace CivicLens.PublicApi.Votes;

/// <summary>One recorded cast on one vote. <c>Party</c> is the party group of the member on the vote, and <c>BreaksParty</c> is <c>public_api.member_votes.breaks_party</c>, the one owner of the break rule.</summary>
public sealed record MemberVoteCast(string OfficialId, string Cast, string? Party, bool? BreaksParty);
