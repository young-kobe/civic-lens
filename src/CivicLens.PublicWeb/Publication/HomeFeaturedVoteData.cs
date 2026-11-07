using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record HomeFeaturedVoteData(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline, string? Label, string Summary,
    PublishedTextOrigin TextOrigin, string ResultWords, int? Yea, int? Nay, string? BillLine, string? BillLineSource);
