namespace CivicLens.Publication.Contracts;

/// <summary>Owns the approved plain meaning of a vote when its cited question and result support it.</summary>
public static class VoteMeaning
{
    public const string Heading = "What this vote meant";
    public const string YesFallback = "A Yes vote supported the question before the chamber.";
    public const string NoFallback = "A No vote opposed the question before the chamber.";

    /// <summary>The index row for the kind Other. It has no Yes meaning, so it sends the reader to the vote page.</summary>
    public const string OtherOnIndex = "The record states the question in its own words. Read it on the vote page.";

    private sealed record Row(PublishedVoteKind Kind, string Yes, string No, string? YesOnIndex = null);

    private static readonly Row[] Table =
    [
        new(PublishedVoteKind.Rule, "A Yes vote supported the rules for considering the measure.", "A No vote opposed the rules for considering the measure."),
        new(PublishedVoteKind.PreviousQuestion, "A Yes vote supported moving the matter toward a vote, with limits on further debate and changes.", "A No vote opposed moving the matter toward a vote this way."),
        new(PublishedVoteKind.MotionToRecommit, "A Yes vote supported sending the measure to a committee.", "A No vote opposed sending the measure to a committee."),
        new(PublishedVoteKind.Amendment, "A Yes vote supported the proposed change.", "A No vote opposed the proposed change."),
        new(PublishedVoteKind.Suspension, "A Yes vote supported the action named in the question under a procedure with limited debate.", "A No vote opposed the action named in the question under a procedure with limited debate.",
            "A Yes vote supported the action under a procedure with limited debate."),
        new(PublishedVoteKind.Passage, "A Yes vote supported passing or agreeing to the matter named in the question.", "A No vote opposed passing or agreeing to the matter named in the question.",
            "A Yes vote supported passing or agreeing to the bill or measure."),
        new(PublishedVoteKind.ConferenceReport, "A Yes vote supported the proposal from members of both chambers on how to resolve their differences.", "A No vote opposed the proposal from members of both chambers on how to resolve their differences."),
        new(PublishedVoteKind.VetoOverride, "A Yes vote supported reversing the President's decision to reject the bill in this chamber.", "A No vote opposed reversing the President's decision to reject the bill in this chamber."),
        new(PublishedVoteKind.Nomination, "A Yes vote supported confirming the nominee.", "A No vote opposed confirming the nominee."),
        new(PublishedVoteKind.Cloture, "A Yes vote supported limiting further debate on the question before the Senate.", "A No vote opposed limiting further debate on the question before the Senate."),
        new(PublishedVoteKind.MotionToProceed, "A Yes vote supported beginning consideration of the matter named in the question.", "A No vote opposed beginning consideration of the matter named in the question.",
            "A Yes vote supported beginning consideration of the bill or measure.")
    ];

    private static readonly IReadOnlyDictionary<PublishedVoteKind, Row> ByKind = Table.ToDictionary(row => row.Kind);

    /// <summary>The kinds this table words, in the one order the table holds them. The kind Other has no row.</summary>
    public static IReadOnlyList<PublishedVoteKind> Kinds { get; } = Table.Select(row => row.Kind).ToArray();

    /// <summary>The Yes meaning of a kind on the vote-kind index. It does not depend on a cited question or result, and it names no question the page does not show.</summary>
    public static string YesOnIndexOf(PublishedVoteKind kind) =>
        ByKind.TryGetValue(kind, out var row) ? row.YesOnIndex ?? row.Yes : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no meaning row.");

    internal static (string Yes, string No) For(PublishedVoteKind kind, VoteWordingInput input)
    {
        if (input.Result is null || !input.KnownQuestion || kind == PublishedVoteKind.Other ||
            !ByKind.TryGetValue(kind, out var row))
            return (YesFallback, NoFallback);
        return (row.Yes, row.No);
    }
}
