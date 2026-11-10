namespace CivicLens.Publication.Contracts;

/// <summary>Owns the approved plain meaning of a vote when its cited question and result support it.</summary>
public static class VoteMeaning
{
    public const string Heading = "What this vote meant";
    public const string YesFallback = "A Yes vote supported the question before the chamber.";
    public const string NoFallback = "A No vote opposed the question before the chamber.";

    private static readonly IReadOnlyDictionary<PublishedVoteKind, (string Yes, string No)> ByKind =
        new Dictionary<PublishedVoteKind, (string Yes, string No)>
        {
            [PublishedVoteKind.Rule] = ("A Yes vote supported the rules for considering the measure.", "A No vote opposed the rules for considering the measure."),
            [PublishedVoteKind.PreviousQuestion] = ("A Yes vote supported moving the matter toward a vote, with limits on further debate and changes.", "A No vote opposed moving the matter toward a vote this way."),
            [PublishedVoteKind.MotionToRecommit] = ("A Yes vote supported sending the measure to a committee.", "A No vote opposed sending the measure to a committee."),
            [PublishedVoteKind.Amendment] = ("A Yes vote supported the proposed change.", "A No vote opposed the proposed change."),
            [PublishedVoteKind.Suspension] = ("A Yes vote supported the action named in the question under a procedure with limited debate.", "A No vote opposed the action named in the question under a procedure with limited debate."),
            [PublishedVoteKind.Passage] = ("A Yes vote supported passing or agreeing to the matter named in the question.", "A No vote opposed passing or agreeing to the matter named in the question."),
            [PublishedVoteKind.ConferenceReport] = ("A Yes vote supported the proposal from members of both chambers on how to resolve their differences.", "A No vote opposed the proposal from members of both chambers on how to resolve their differences."),
            [PublishedVoteKind.VetoOverride] = ("A Yes vote supported reversing the President's decision to reject the bill in this chamber.", "A No vote opposed reversing the President's decision to reject the bill in this chamber."),
            [PublishedVoteKind.Nomination] = ("A Yes vote supported confirming the nominee.", "A No vote opposed confirming the nominee."),
            [PublishedVoteKind.Cloture] = ("A Yes vote supported limiting further debate on the question before the Senate.", "A No vote opposed limiting further debate on the question before the Senate."),
            [PublishedVoteKind.MotionToProceed] = ("A Yes vote supported beginning consideration of the matter named in the question.", "A No vote opposed beginning consideration of the matter named in the question.")
        };

    internal static (string Yes, string No) For(PublishedVote vote, VoteWordingInput input)
    {
        if (input.Result is null || !input.KnownQuestion || vote.Kind == PublishedVoteKind.Other ||
            !ByKind.TryGetValue(vote.Kind, out var meaning))
            return (YesFallback, NoFallback);
        return meaning;
    }
}
