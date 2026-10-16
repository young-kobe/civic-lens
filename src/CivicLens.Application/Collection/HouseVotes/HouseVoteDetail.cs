namespace CivicLens.Application.Collection.HouseVotes;

public sealed record HouseVoteDetail(
    HouseVote Vote,
    DateTimeOffset? StartDate,
    string? Question,
    string? VoteType,
    string? Result,
    string? LegislationType,
    string? LegislationNumber,
    IReadOnlyList<HouseVotePartyTotal> PartyTotals,
    IReadOnlyList<HouseVoteCandidateTotal> CandidateTotals,
    string? SourceDataUrl);
