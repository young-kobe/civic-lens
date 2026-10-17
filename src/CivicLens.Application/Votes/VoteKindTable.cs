using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteKindTable
{
    private const string AgreeingToResolution = "On Agreeing to the Resolution";

    private static readonly (string QuestionStart, VoteKind Kind)[] ByQuestionStart =
    [
        ("On Motion to Suspend the Rules", VoteKind.Suspension),
        ("On Ordering the Previous Question", VoteKind.PreviousQuestion),
        ("On Motion to Recommit", VoteKind.MotionToRecommit),
        ("On Motion to Commit", VoteKind.MotionToRecommit),
        ("On Motion to Concur in the Senate Amendment", VoteKind.Passage),
        ("On Agreeing to the Amendment", VoteKind.Amendment),
        ("On Agreeing to the Conference Report", VoteKind.ConferenceReport),
        ("Passage, Objections of the President", VoteKind.VetoOverride),
        ("On Passage", VoteKind.Passage)
    ];

    public static (VoteKind Kind, SourceSpan? Citation) Classify(ImmutableArray<VoteFact> facts)
    {
        var question = VoteFacts.Find(facts, VoteFactPrefixes.Question);
        if (question is null) return (VoteKind.Other, null);
        return (KindOf(question.Text[VoteFactPrefixes.Question.Length..], facts), question.Citation);
    }

    private static VoteKind KindOf(string question, ImmutableArray<VoteFact> facts)
    {
        if (question.StartsWith(AgreeingToResolution, StringComparison.OrdinalIgnoreCase))
            return ProvidesForConsideration(facts) ? VoteKind.Rule : VoteKind.Passage;
        foreach (var (questionStart, kind) in ByQuestionStart)
            if (question.StartsWith(questionStart, StringComparison.OrdinalIgnoreCase)) return kind;
        return VoteKind.Other;
    }

    private static bool ProvidesForConsideration(ImmutableArray<VoteFact> facts)
    {
        var isHouseResolution = VoteFacts.Find(facts, VoteFactPrefixes.Legislation)?.Text[VoteFactPrefixes.Legislation.Length..]
            .StartsWith("HRES ", StringComparison.OrdinalIgnoreCase) == true;
        var title = VoteFacts.Find(facts, VoteFactPrefixes.Title)?.Text[VoteFactPrefixes.Title.Length..];
        return isHouseResolution && title?.Contains("consideration of", StringComparison.OrdinalIgnoreCase) == true;
    }
}
