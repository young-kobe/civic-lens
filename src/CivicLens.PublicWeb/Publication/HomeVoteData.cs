using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record HomeVoteData(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline, string? Label,
    string ResultWords, int Yea, int Nay, string? BillLine, string? BillLineSource)
{
    public PublishedVoteAlignment? Alignment { get; init; }
}
