using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the mark of a seat and its legend words. A seat with no member row is never a cast.</summary>
public static class VoteMapMarks
{
    public const string NoMember = "No member in the record";

    public static VoteMapMark Of(VoteMemberData? member) => member is null ? VoteMapMark.NoMember : PlainVoteWords.CastWord(member.Cast) switch
    {
        PlainVoteWords.Yes => VoteMapMark.Yes,
        PlainVoteWords.No => VoteMapMark.No,
        PlainVoteWords.Present => VoteMapMark.Present,
        PlainVoteWords.NotVoting => VoteMapMark.NotVoting,
        _ => VoteMapMark.Other
    };

    public static string Label(VoteMapMark mark) => mark switch
    {
        VoteMapMark.Yes => PlainVoteWords.Yes,
        VoteMapMark.No => PlainVoteWords.No,
        VoteMapMark.Present => PlainVoteWords.Present,
        VoteMapMark.NotVoting => PlainVoteWords.NotVoting,
        VoteMapMark.Other => PlainVoteWords.OtherCast,
        _ => NoMember
    };

    /// <summary>The party tone of a seat in the party color mode. It follows the one party color rule. A seat with no member has no party and reads as neutral.</summary>
    public static PartyTone Party(VoteMemberData? member) => member?.Party is { } party ? PartyTones.Of(party) : PartyTone.Neutral;

    /// <summary>The name of the party tone in markup, for the color of a seat in the party color mode.</summary>
    public static string Name(PartyTone tone) => tone.ToString().ToLowerInvariant();

    /// <summary>The name of the mark in markup, for its style and its pattern.</summary>
    public static string Name(VoteMapMark mark) => mark switch
    {
        VoteMapMark.NotVoting => "not-voting",
        VoteMapMark.NoMember => "no-member",
        _ => mark.ToString().ToLowerInvariant()
    };
}

/// <summary>The look of a seat. Yes is solid ink, No is orange stripes, Present and Not voting are two dot patterns, Other is a grid, and a seat with no member row is a plain fill with a dashed line.</summary>
public enum VoteMapMark
{
    Yes,
    No,
    Present,
    NotVoting,
    Other,
    NoMember
}
