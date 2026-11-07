using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record HomeVoteData(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline,
    string ResultWords, int Yea, int Nay, string? BillLine, string? BillLineSource);
