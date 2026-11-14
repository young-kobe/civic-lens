using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>One substantive vote and the asked officials' recorded casts for it. <c>TextOrigin</c> says who wrote the headline.</summary>
public sealed record SharedMemberVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, PublishedVoteKind Kind, string KindWords,
    string? BillType, int? BillNumber, string Headline, string? Label, PublishedTextOrigin TextOrigin, string Result, string ResultWords, SharedMemberVoteCast[] Casts);
