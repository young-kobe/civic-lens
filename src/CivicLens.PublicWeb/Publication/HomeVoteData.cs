using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One recent vote row of Home. <c>Totals</c> is the chamber bar of <see cref="VotePageView.ChamberBar(PublishedChamber, int, int, int, int, VoteThreshold)"/>, or null when the release holds no counts for the vote.
/// <c>TextOrigin</c> says who wrote <c>Label</c>, so the shared record row colors a model label.</summary>
public sealed record HomeVoteData(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, string Headline, string? Label,
    PublishedTextOrigin TextOrigin, string ResultWords, PartyBarData? Totals, string? BillLine)
{
    public PublishedVoteAlignment? Alignment { get; init; }
}
