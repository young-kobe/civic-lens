using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class VoteMeaningTests
{
    [Fact]
    public void EveryKindWithAKnownQuestionHasADistinctYesAndNoSentenceSoNoKindReadsLikeAnother()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>().Where(kind => kind != PublishedVoteKind.Other).ToArray();

        var meanings = kinds.Select(kind => VoteMeaning.Of(kind)!.Value).ToArray();

        Assert.All(meanings, meaning => Assert.StartsWith("A Yes vote", meaning.Yes, StringComparison.Ordinal));
        Assert.All(meanings, meaning => Assert.StartsWith("A No vote", meaning.No, StringComparison.Ordinal));
        Assert.Equal(kinds.Length, meanings.Select(meaning => meaning.Yes).Distinct().Count());
        Assert.Equal(kinds.Length, meanings.Select(meaning => meaning.No).Distinct().Count());
    }

    [Theory]
    [InlineData(PublishedVoteKind.Other)]
    [InlineData((PublishedVoteKind)99)]
    public void AKindWithNoKnownQuestionGivesNoWordsBecauseAGuessWouldMisleadTheReader(PublishedVoteKind kind) =>
        Assert.Null(VoteMeaning.Of(kind));

    public static TheoryData<PublishedVoteKind, string, string> Meanings => new()
    {
        { PublishedVoteKind.Rule, "A Yes vote was for the rules for debate on a bill, such as the time limit and which changes members may offer.", "A No vote was against those rules." },
        { PublishedVoteKind.PreviousQuestion, "A Yes vote was for ending debate so that members vote now.", "A No vote was for letting the minority offer a change to the rule." },
        { PublishedVoteKind.MotionToRecommit, "A Yes vote was for sending the bill back to its committee.", "A No vote was against sending the bill back to its committee." },
        { PublishedVoteKind.Amendment, "A Yes vote was for adding the change to the bill.", "A No vote was against adding the change to the bill." },
        { PublishedVoteKind.Suspension, "A Yes vote was for passing the bill with limited debate and no changes from the floor. This route needs two thirds of the members who vote.", "A No vote was against passing the bill this way." },
        { PublishedVoteKind.Passage, "A Yes vote was for passing the bill or agreeing to the measure.", "A No vote was against it." },
        { PublishedVoteKind.ConferenceReport, "A Yes vote was for accepting the final text that both chambers worked out together.", "A No vote was against accepting that text." },
        { PublishedVoteKind.VetoOverride, "A Yes vote was for passing the bill over the veto of the President.", "A No vote was against passing the bill over the veto." },
        { PublishedVoteKind.Nomination, "A Yes vote was for confirming the nominee.", "A No vote was against confirming the nominee." },
        { PublishedVoteKind.Cloture, "A Yes vote was for limiting further debate on the matter.", "A No vote was against limiting further debate on the matter." },
        { PublishedVoteKind.MotionToProceed, "A Yes vote was for starting debate on the matter.", "A No vote was against starting debate on it." },
    };

    [Theory]
    [MemberData(nameof(Meanings))]
    public void EachKindGivesItsExactYesAndNoSentenceSoASwapOfTwoKindsFails(PublishedVoteKind kind, string yes, string no)
    {
        var meaning = VoteMeaning.Of(kind)!.Value;

        Assert.Equal(yes, meaning.Yes);
        Assert.Equal(no, meaning.No);
    }

    [Fact]
    public void EverySentenceSaysWhatTheVoteWasForOrAgainstAndNeverWhatItDidBecauseTheResultIsNotAnInput()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>().Where(kind => kind != PublishedVoteKind.Other);

        var texts = kinds.Select(kind => VoteMeaning.Of(kind)!.Value).SelectMany(meaning => new[] { meaning.Yes, meaning.No }).ToArray();

        Assert.All(texts, text => Assert.True(
            text.Contains("was for", StringComparison.Ordinal) || text.Contains("was against", StringComparison.Ordinal), text));
    }

    [Fact]
    public void EveryKindWithAKnownQuestionHasARowInTheExactTable() =>
        Assert.Equal(Enum.GetValues<PublishedVoteKind>().Count(kind => kind != PublishedVoteKind.Other), Meanings.Count);
}
