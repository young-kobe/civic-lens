using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedVoteLabelsTests
{
    [Fact]
    public void EachVoteKindExceptOtherHasOneDistinctPlainQuestionSoNoQuestionReadsLikeAnother()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>().Where(kind => kind != PublishedVoteKind.Other).ToArray();

        var words = kinds.Select(PublishedRecordLabels.VoteQuestionWords).ToArray();

        Assert.All(words, Assert.NotNull);
        Assert.Equal(kinds.Length, words.Distinct().Count());
        Assert.Null(PublishedRecordLabels.VoteQuestionWords(PublishedVoteKind.Other));
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "Pass the bill (On Passage)")]
    [InlineData(PublishedVoteKind.Nomination, "On the Nomination", "Confirm the nominee (On the Nomination)")]
    [InlineData(PublishedVoteKind.Other, "On the Motion to Table", "On the Motion to Table")]
    public void AQuestionShowsPlainWordsFirstAndTheSourceWordsInBracketsAndAKindWithNoPlainWordsShowsTheSourceAlone(PublishedVoteKind kind, string source, string expected) =>
        Assert.Equal(expected, PublishedRecordLabels.VoteQuestion(kind, source));

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("Failed", "Failed")]
    [InlineData("Agreed to", "Approved (Agreed to)")]
    [InlineData("Cloture Motion Agreed to", "Approved (Cloture Motion Agreed to)")]
    [InlineData("Nomination Confirmed", "Nominee approved (Nomination Confirmed)")]
    [InlineData("Bill Passed", "Passed (Bill Passed)")]
    [InlineData("Rejected", "Turned down (Rejected)")]
    [InlineData("Point of Order Sustained", "Point of Order Sustained")]
    [InlineData("Not Agreed to", "Not Agreed to")]
    public void AResultShowsPlainWordsFirstAndTheSourceWordsInBracketsAndAnUnknownResultShowsTheSourceAlone(string source, string expected) =>
        Assert.Equal(expected, PublishedRecordLabels.VoteResult(source));
}
