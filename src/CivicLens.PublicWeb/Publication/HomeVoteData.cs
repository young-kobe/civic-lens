using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One recent vote row of Home. <c>Totals</c> is the chamber bar of <see cref="VotePageView.ChamberBar(PublishedChamber, int, int, int, int, bool)"/>, or null when the release holds no counts for the vote.</summary>
public sealed record HomeVoteData(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline, string? Label,
    string ResultWords, PartyBarData? Totals, string? BillLine)
{
    public PublishedVoteAlignment? Alignment { get; init; }
}
