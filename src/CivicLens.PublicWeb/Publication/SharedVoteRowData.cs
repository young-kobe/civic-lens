using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One vote row. <c>Totals</c> is the chamber bar of all casts and <c>Alignment</c> gives the split badge. A page that has no totals for the vote leaves both null.
/// <c>Chamber</c> names the chamber in the roll call meta, for a list that can mix House and Senate votes.
/// <c>TextOrigin</c> is the view <c>text_origin</c> of the headline. Only a model label takes the AI ink, as in a list row.
/// </summary>
public sealed record SharedVoteRowData(
    string RecordId, DateOnly VotedOn, int Congress, int Session, int RollCall, string Headline, string? Label,
    PublishedTextOrigin TextOrigin, string KindWords, string ResultWords, SharedVoteCastData[] Casts)
{
    public bool? MembersVotedTheSame { get; init; }
    public PublishedVoteAlignment? Alignment { get; init; }
    public PartyBarData? Totals { get; init; }
    public PublishedChamber? Chamber { get; init; }
}
