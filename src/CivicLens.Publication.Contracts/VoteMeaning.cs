namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of what a Yes vote and a No vote meant, by vote kind. The sentences are code, never a model prediction.
/// The words do not depend on the result: a Yes vote meant the same whether the question passed or failed. Each sentence says what the vote was for or against, never what it did.
/// The kind Other has no words, because its question is unknown. Public wording is the owner's choice, so a change is an edit of this table.
/// </summary>
public static class VoteMeaning
{
    /// <summary>The title of the vote page panel that shows the meaning and the next step.</summary>
    public const string Heading = "What this vote meant";

    private static readonly Dictionary<PublishedVoteKind, (string Yes, string No)> ByKind = new()
    {
        [PublishedVoteKind.Rule] = (
            "A Yes vote was for the rules for debate on a bill, such as the time limit and which changes members may offer.",
            "A No vote was against those rules."),
        [PublishedVoteKind.PreviousQuestion] = (
            "A Yes vote was for ending debate so that members vote now.",
            "A No vote was for letting the minority offer a change to the rule."),
        [PublishedVoteKind.MotionToRecommit] = (
            "A Yes vote was for sending the bill back to its committee.",
            "A No vote was against sending the bill back to its committee."),
        [PublishedVoteKind.Amendment] = (
            "A Yes vote was for adding the change to the bill.",
            "A No vote was against adding the change to the bill."),
        [PublishedVoteKind.Suspension] = (
            "A Yes vote was for passing the bill with limited debate and no changes from the floor. This route needs two thirds of the members who vote.",
            "A No vote was against passing the bill this way."),
        [PublishedVoteKind.Passage] = (
            "A Yes vote was for passing the bill or agreeing to the measure.",
            "A No vote was against it."),
        [PublishedVoteKind.ConferenceReport] = (
            "A Yes vote was for accepting the final text that both chambers worked out together.",
            "A No vote was against accepting that text."),
        [PublishedVoteKind.VetoOverride] = (
            "A Yes vote was for passing the bill over the veto of the President.",
            "A No vote was against passing the bill over the veto."),
        [PublishedVoteKind.Nomination] = (
            "A Yes vote was for confirming the nominee.",
            "A No vote was against confirming the nominee."),
        [PublishedVoteKind.Cloture] = (
            "A Yes vote was for limiting further debate on the matter.",
            "A No vote was against limiting further debate on the matter."),
        [PublishedVoteKind.MotionToProceed] = (
            "A Yes vote was for starting debate on the matter.",
            "A No vote was against starting debate on it.")
    };

    /// <summary>The meaning of a Yes vote and of a No vote, or null for a kind with no words. It never guesses.</summary>
    public static (string Yes, string No)? Of(PublishedVoteKind kind) => ByKind.TryGetValue(kind, out var meaning) ? meaning : null;
}
