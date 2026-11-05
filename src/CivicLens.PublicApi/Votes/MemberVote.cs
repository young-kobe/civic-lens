using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>One vote with the cast of the official. <c>Cast</c> is the source word. <c>CastWord</c> is the same cast for a reader.</summary>
public sealed record MemberVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, PublishedVoteKind Kind,
    string? BillType, int? BillNumber, string Headline, string? Label, string Result, string Cast, string CastWord);
