using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the fixed words that vote pages and vote lists share: the split and agreed badges, the party count note, and the break rule.</summary>
public static class VoteWords
{
    public const string SplitBadge = "Split vote";
    public const string PartiesAgreedBadge = "Both parties agreed";

    public static readonly string BothMajorities = $"most {PublishedPartyMajority.Democrat}s and most {PublishedPartyMajority.Republican}s who cast Yes or No";
    public static readonly string SplitMeaning = $"A split vote is a substantive roll call vote where {BothMajorities} voted on opposite sides.";
    public static readonly string PartiesAgreedMeaning = $"A vote where both parties agreed is a substantive roll call vote where {BothMajorities} voted the same way.";

    public const string PartyCountsNote = "These counts include every member of the party, tracked or not. An independent counts with the party they caucus with, so these counts can differ from the source totals. Yes here means Yea or Aye in the record. No means Nay.";

    public const string BreakMeaning = "A member is listed when they cast Yes or No against the cast of most members of their own party on this vote. A current independent counts with the party they caucus with. A former independent counts with no party. A member with another party, or with no known party, is not listed. Present and Not voting never count as a break.";

    public const string TrackedOnly = "This record lists only the officials that Civic Lens tracks.";

    /// <summary>The badge text and meaning of a stored alignment. A mixed alignment has no badge.</summary>
    public static (string Text, string Meaning)? Badge(PublishedVoteAlignment? alignment) => alignment switch
    {
        PublishedVoteAlignment.Split => (SplitBadge, SplitMeaning),
        PublishedVoteAlignment.PartiesAgree => (PartiesAgreedBadge, PartiesAgreedMeaning),
        _ => null
    };
}
