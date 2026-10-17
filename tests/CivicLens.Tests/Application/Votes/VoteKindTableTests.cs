using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteKindTableTests
{
    private static readonly SourceSpan QuestionSpan = new(new string('c', 64), 40, 30);

    [Theory]
    [InlineData("On Passage", VoteKind.Passage)]
    [InlineData("On Motion to Suspend the Rules and Pass", VoteKind.Suspension)]
    [InlineData("On Motion to Suspend the Rules and Pass, as Amended", VoteKind.Suspension)]
    [InlineData("On Motion to Suspend the Rules and Concur in the Senate Amendments", VoteKind.Suspension)]
    [InlineData("On Ordering the Previous Question", VoteKind.PreviousQuestion)]
    [InlineData("On Motion to Recommit", VoteKind.MotionToRecommit)]
    [InlineData("On Motion to Commit", VoteKind.MotionToRecommit)]
    [InlineData("On Motion to Commit with Instructions", VoteKind.MotionToRecommit)]
    [InlineData("On Motion to Concur in the Senate Amendment", VoteKind.Passage)]
    [InlineData("On Motion to Concur in the Senate Amendments", VoteKind.Passage)]
    [InlineData("On Consideration of the Resolution", VoteKind.Other)]
    [InlineData("On Agreeing to the Amendment", VoteKind.Amendment)]
    [InlineData("On Agreeing to the Amendment, as Modified", VoteKind.Amendment)]
    [InlineData("On Agreeing to the Conference Report", VoteKind.ConferenceReport)]
    [InlineData("Passage, Objections of the President To The Contrary Notwithstanding", VoteKind.VetoOverride)]
    [InlineData("Election of the Speaker", VoteKind.Other)]
    [InlineData("Call of the House", VoteKind.Other)]
    [InlineData("On Motion to Adjourn", VoteKind.Other)]
    [InlineData("On Motion to Table", VoteKind.Other)]
    [InlineData("A question that no table row knows", VoteKind.Other)]
    public void EachQuestionLineMapsToTheKindThatNamesItsStageSoTheReviewerCanGroupVotesByWhatTheyDecided(string question, VoteKind expected)
    {
        var (kind, citation) = VoteKindTable.Classify([Fact($"Question: {question}")]);

        Assert.Equal((expected, QuestionSpan), (kind, citation));
    }

    [Fact]
    public void AnUnknownQuestionStillCitesItsLineBecauseTheReviewerCanThenSeeWhyTheKindIsOther()
    {
        var (kind, citation) = VoteKindTable.Classify([Fact("Question: Unheard of")]);

        Assert.Equal((VoteKind.Other, QuestionSpan), (kind, citation));
    }

    [Fact]
    public void ARollCallWithoutAQuestionLineIsOtherWithNoCitation()
    {
        var (kind, citation) = VoteKindTable.Classify([new("Result: Passed", new SourceSpan(new string('c', 64), 0, 14))]);

        Assert.Equal((VoteKind.Other, null), (kind, citation));
    }

    [Theory]
    [InlineData("Providing for consideration of the bill (H.R. 1101) to prohibit unlawful acts.")]
    [InlineData("Providing for further consideration of the bill (H.R. 1) to reduce taxes.")]
    [InlineData("Waiving a requirement of clause 6(a) of rule XIII with respect to consideration of certain resolutions reported from the Committee on Rules.")]
    [InlineData("Relating to consideration of the Senate amendment to the bill (H.R. 5) to fund the government.")]
    public void AHouseResolutionThatProvidesForConsiderationIsARuleBecauseItOnlySetsTheTermsOfDebate(string title)
    {
        var (kind, _) = VoteKindTable.Classify(Resolution("HRES 580", title));

        Assert.Equal(VoteKind.Rule, kind);
    }

    [Theory]
    [InlineData("HRES 1009", "Providing for the expulsion of Representative Jane Doe from the House.")]
    [InlineData("HRES 1010", "Condemning the attack on a city.")]
    [InlineData("HCONRES 38", "Providing for consideration of the budget in a joint session.")]
    public void AResolutionThatIsNotARuleIsPassageBecauseAgreeingToItIsItsFinalVote(string legislation, string title)
    {
        var (kind, _) = VoteKindTable.Classify(Resolution(legislation, title));

        Assert.Equal(VoteKind.Passage, kind);
    }

    [Fact]
    public void AResolutionWithoutABillTitleIsNotARuleBecauseNothingShowsItProvidesForConsideration()
    {
        var (kind, _) = VoteKindTable.Classify([Fact("Question: On Agreeing to the Resolution"), Fact("Legislation: HRES 580")]);

        Assert.Equal(VoteKind.Passage, kind);
    }

    private static ImmutableArray<VoteFact> Resolution(string legislation, string title) =>
        [Fact("Question: On Agreeing to the Resolution"), Fact($"Legislation: {legislation}"), Fact($"Title: {title}")];

    private static VoteFact Fact(string text) => new(text, text.StartsWith("Question: ", StringComparison.Ordinal) ? QuestionSpan
        : new SourceSpan(new string('d', 64), 0, text.Length));
}
