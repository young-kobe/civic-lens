using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>One vote with the cast of the official. <c>Kind</c>, <c>Result</c>, and <c>Cast</c> are the source words.
/// <c>KindWords</c>, <c>ResultWords</c>, and <c>CastWords</c> are the plain words from <see cref="PlainVoteWords"/>, such as "Yes (Aye)".</summary>
public sealed record MemberVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, PublishedVoteKind Kind, string KindWords,
    string? BillType, int? BillNumber, string Headline, string? Label, string Result, string ResultWords, string Cast, string CastWords, string? Side,
    string? PartyMajorityCast);
