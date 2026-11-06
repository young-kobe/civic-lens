using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>The latest published passage vote on a bill, including its approved summary and wording origin.</summary>
public sealed record HomeFeaturedVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, string Headline, string? Label,
    string Summary, PublishedTextOrigin TextOrigin, string Result, string ResultWords, int? Yea, int? Nay,
    string? BillType, int? BillNumber, string? BillLine, string? BillLineSource);
