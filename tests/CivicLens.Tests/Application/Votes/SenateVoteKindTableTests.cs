using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

public sealed class SenateVoteKindTableTests
{
    private static readonly SourceSpan QuestionSpan = new(new string('c', 64), 40, 30);

    [Theory]
    [InlineData("On the Cloture Motion", VoteKind.Cloture)]
    [InlineData("On Cloture on the Motion to Proceed", VoteKind.Cloture)]
    [InlineData("On the Motion to Proceed", VoteKind.MotionToProceed)]
    [InlineData("On the Nomination", VoteKind.Nomination)]
    [InlineData("On the Amendment", VoteKind.Amendment)]
    [InlineData("On the Conference Report", VoteKind.ConferenceReport)]
    [InlineData("On Passage of the Bill", VoteKind.Passage)]
    [InlineData("On the Joint Resolution", VoteKind.Passage)]
    [InlineData("On the Concurrent Resolution", VoteKind.Passage)]
    [InlineData("On the Resolution", VoteKind.Passage)]
    [InlineData("On Overriding the Veto", VoteKind.VetoOverride)]
    [InlineData("On the Resolution of Ratification", VoteKind.Other)]
    [InlineData("On the Motion to Table", VoteKind.Other)]
    [InlineData("On the Motion to Reconsider", VoteKind.Other)]
    [InlineData("On the Decision of the Chair", VoteKind.Other)]
    [InlineData("A question that no table row knows", VoteKind.Other)]
    public void EachSenateQuestionMapsToTheKindThatNamesItsStageSoTheReviewerCanGroupVotesByWhatTheyDecided(string question, VoteKind expected)
    {
        var (kind, citation) = SenateVoteKindTable.Classify([Fact($"Question: {question}")]);

        Assert.Equal((expected, QuestionSpan), (kind, citation));
    }

    [Fact]
    public void AClotureMotionOnANominationIsClotureBecauseTheVoteDecidesToEndDebateNotToConfirm()
    {
        var (kind, _) = SenateVoteKindTable.Classify([Fact("Question: On the Cloture Motion"), Fact("Document: PN373")]);

        Assert.Equal(VoteKind.Cloture, kind);
    }

    [Fact]
    public void ARatificationVoteIsNotPassageBecauseAgreeingToATreatyIsNotPassingALaw()
    {
        var (kind, _) = SenateVoteKindTable.Classify([Fact("Question: On the Resolution of Ratification")]);

        Assert.NotEqual(VoteKind.Passage, kind);
    }

    [Fact]
    public void ASenateRollCallWithoutAQuestionLineIsOtherWithNoCitation()
    {
        var (kind, citation) = SenateVoteKindTable.Classify([new("Result: Confirmed", new SourceSpan(new string('c', 64), 0, 17))]);

        Assert.Equal((VoteKind.Other, null), (kind, citation));
    }

    private static VoteFact Fact(string text) => new(text, text.StartsWith("Question: ", StringComparison.Ordinal) ? QuestionSpan
        : new SourceSpan(new string('d', 64), 0, text.Length));
}
