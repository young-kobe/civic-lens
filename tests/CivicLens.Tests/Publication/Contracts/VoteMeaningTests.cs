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

    [Fact]
    public void AMotionToRecommitYesVoteSentTheBillBackToCommitteeAndANoVoteKeptItBeforeTheChamber()
    {
        var meaning = VoteMeaning.Of(PublishedVoteKind.MotionToRecommit)!.Value;

        Assert.Equal("A Yes vote sent the bill back to its committee.", meaning.Yes);
        Assert.Equal("A No vote kept the bill before the full chamber.", meaning.No);
    }
}
