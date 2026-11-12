using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One vote row. <c>Totals</c> is the chamber bar of all casts and <c>Alignment</c> gives the split badge. A page that has no totals for the vote leaves both null.</summary>
public sealed record SharedVoteRowData(
    string RecordId, DateOnly VotedOn, int Congress, int Session, int RollCall, string Headline, string? Label,
    string KindWords, string ResultWords, SharedVoteCastData[] Casts)
{
    public bool? MembersVotedTheSame { get; init; }
    public PublishedVoteAlignment? Alignment { get; init; }
    public PartyBarData? Totals { get; init; }
}
