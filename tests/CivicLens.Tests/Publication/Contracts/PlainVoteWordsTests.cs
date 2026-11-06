using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PlainVoteWordsTests
{
    [Theory]
    [InlineData("Aye", "Yes (Aye)")]
    [InlineData("Yea", "Yes (Yea)")]
    [InlineData("Nay", "No (Nay)")]
    [InlineData("No", "No")]
    [InlineData("Present", "Present")]
    [InlineData("Not Voting", "Not voting")]
    [InlineData("Sen. Smith", "Sen. Smith")]
    public void ACastShowsThePlainWordFirstWithTheSourceWordInBracketsAndNoBracketsWhenBothWordsMatchIgnoringCase(string cast, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Cast(cast));

    [Fact]
    public void TheSourceCastNotVotingHasThePlainWordAndAnyOtherSpellingHasNone()
    {
        Assert.Equal(PlainVoteWords.NotVoting, PlainVoteWords.CastWord("Not Voting"));
        Assert.Null(PlainVoteWords.CastWord("Not voting"));
    }

    [Fact]
    public void EveryPlainResultWordComesOnceAndALongerWordComesBeforeAWordItEndsWithSoASearchFindsNotConfirmedBeforeConfirmed()
    {
        var words = PlainVoteWords.AllResultWords;

        Assert.Equal(words.Count, words.Distinct().Count());
        Assert.Equal(words.OrderByDescending(word => word.Length), words);
        Assert.True(words.ToList().IndexOf("Not confirmed") < words.ToList().IndexOf("Confirmed"));
        Assert.All(words, word => Assert.Equal(word, PlainVoteWords.ResultWord(word)));
    }

    [Fact]
    public void EachVoteKindHasOneDistinctPlainNameSoNoNameReadsLikeAnother()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>();

        var names = kinds.Select(PlainVoteWords.Kind).ToArray();

        Assert.Equal(kinds.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "Vote to pass or agree to")]
    [InlineData(PublishedVoteKind.Cloture, "Vote to end debate")]
    [InlineData(PublishedVoteKind.Nomination, "Vote to confirm")]
    [InlineData(PublishedVoteKind.Other, "Other question")]
    public void AKindNameIsPlain(PublishedVoteKind kind, string expected) => Assert.Equal(expected, PlainVoteWords.Kind(kind));

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "Vote to pass or agree to (On Passage)")]
    [InlineData(PublishedVoteKind.Passage, "On Motion to Concur in the Senate Amendment", "Vote to pass or agree to (On Motion to Concur in the Senate Amendment)")]
    [InlineData(PublishedVoteKind.Cloture, "On the Cloture Motion", "Vote to end debate (On the Cloture Motion)")]
    [InlineData(PublishedVoteKind.Nomination, "On the Nomination", "Vote to confirm (On the Nomination)")]
    [InlineData(PublishedVoteKind.Other, "On the Motion to Table", "On the Motion to Table")]
    public void AQuestionShowsThePlainKindNameFirstAndTheSourceQuestionInBracketsAndKindOtherShowsTheSourceAlone(PublishedVoteKind kind, string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Question(kind, source));

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("passed", "Passed")]
    [InlineData("Failed", "Failed")]
    [InlineData("Agreed to", "Passed (Agreed to)")]
    [InlineData("Cloture Motion Agreed to", "Passed (Cloture Motion Agreed to)")]
    [InlineData("Bill Passed", "Passed (Bill Passed)")]
    [InlineData("Confirmed", "Confirmed")]
    [InlineData("Nomination Confirmed", "Confirmed (Nomination Confirmed)")]
    [InlineData("Nomination Rejected", "Not confirmed (Nomination Rejected)")]
    [InlineData("Nomination Not Confirmed", "Not confirmed (Nomination Not Confirmed)")]
    [InlineData("Not Agreed to", "Failed (Not Agreed to)")]
    [InlineData("Rejected", "Failed (Rejected)")]
    [InlineData("Amendment Rejected", "Failed (Amendment Rejected)")]
    [InlineData("Cloture Motion Rejected", "Failed (Cloture Motion Rejected)")]
    [InlineData("  Passed ", "Passed")]
    public void AResultShowsThePlainWordsFirstAndTheSourceWordsInBrackets(string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Result(source));

    [Theory]
    [InlineData("Point of Order Sustained")]
    [InlineData("Not Passed")]
    [InlineData("Unpassed")]
    [InlineData("Passed <b>")]
    public void AnUnknownResultShowsTheSourceWordsAlone(string source) => Assert.Equal(source, PlainVoteWords.Result(source));

    [Fact]
    public void ResultsNeverUseTheWordApprovedBecauseThatWordIsForReviewStatus()
    {
        var sources = new[] { "Passed", "Failed", "Agreed to", "Confirmed", "Rejected", "Not Agreed to", "Nomination Rejected", "Not Confirmed" };

        Assert.All(sources, source => Assert.DoesNotContain("Approved", PlainVoteWords.Result(source), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(210, 200, 0, 5, "Yes 210, No 200, Not voting 5")]
    [InlineData(210, 200, 2, 5, "Yes 210, No 200, Present 2, Not voting 5")]
    public void TotalsUsePlainWordsAndShowPresentOnlyWhenItIsNotZero(int yea, int nay, int present, int notVoting, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Totals(new() { Yea = yea, Nay = nay, Present = present, NotVoting = notVoting }));

    [Theory]
    [InlineData("Yes", "Yes", "Yes")]
    [InlineData("Yes", "yes", "Yes")]
    [InlineData("Yes", "Aye", "Yes (Aye)")]
    [InlineData(null, "Aye", "Aye")]
    public void TheFormatPutsThePlainWordFirstAndShowsEqualWordsOnceInThePlainSpelling(string? plain, string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.PlainWithSource(plain, source));
}
