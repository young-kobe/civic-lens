using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class SenateVoteKindTable
{
    private static readonly (string QuestionStart, VoteKind Kind)[] ByQuestionStart =
    [
        ("On the Cloture Motion", VoteKind.Cloture),
        ("On Cloture", VoteKind.Cloture),
        ("On the Motion to Proceed", VoteKind.MotionToProceed),
        ("On the Nomination", VoteKind.Nomination),
        ("On the Amendment", VoteKind.Amendment),
        ("On the Conference Report", VoteKind.ConferenceReport),
        ("On Overriding the Veto", VoteKind.VetoOverride),
        ("On Passage of the", VoteKind.Passage),
        ("On the Joint Resolution", VoteKind.Passage),
        ("On the Concurrent Resolution", VoteKind.Passage),
        ("On the Resolution of Ratification", VoteKind.Other),
        ("On the Resolution", VoteKind.Passage)
    ];

    public static (VoteKind Kind, SourceSpan? Citation) Classify(ImmutableArray<VoteFact> facts)
    {
        var question = VoteFacts.Find(facts, VoteFactPrefixes.Question);
        if (question is null) return (VoteKind.Other, null);
        return (KindOf(question.Text[VoteFactPrefixes.Question.Length..]), question.Citation);
    }

    private static VoteKind KindOf(string question)
    {
        foreach (var (questionStart, kind) in ByQuestionStart)
            if (question.StartsWith(questionStart, StringComparison.OrdinalIgnoreCase)) return kind;
        return VoteKind.Other;
    }
}
