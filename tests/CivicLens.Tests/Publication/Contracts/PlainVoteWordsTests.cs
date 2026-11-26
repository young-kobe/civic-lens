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
    public void EachVoteKindHasOneDistinctPlainNameSoNoNameReadsLikeAnother()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>();

        var names = kinds.Select(PlainVoteWords.Kind).ToArray();

        Assert.Equal(kinds.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void RecordTermsCoverEveryNamedKindAndOtherHasNoSingleTerm()
    {
        Assert.Equal(
            ["Rule", "Previous question", "Motion to recommit", "Motion to table", "Motion to discharge", "Amendment", "Suspension of the rules", "Passage or agreement", "Conference report", "Veto override", "Nomination", "Cloture", "Motion to proceed"],
            Enum.GetValues<PublishedVoteKind>().Where(kind => kind != PublishedVoteKind.Other).Select(PlainVoteWords.RecordTerm));
        Assert.Null(PlainVoteWords.RecordTerm(PublishedVoteKind.Other));
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "Vote to pass or agree to")]
    [InlineData(PublishedVoteKind.MotionToTable, "Vote to set aside and reject")]
    [InlineData(PublishedVoteKind.MotionToDischarge, "Vote to pull from committee")]
    [InlineData(PublishedVoteKind.Cloture, "Vote to end debate")]
    [InlineData(PublishedVoteKind.Nomination, "Vote to confirm")]
    [InlineData(PublishedVoteKind.Other, "Other question")]
    public void AKindNameIsPlain(PublishedVoteKind kind, string expected) => Assert.Equal(expected, PlainVoteWords.Kind(kind));

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "Vote to pass or agree to (On Passage)")]
    [InlineData(PublishedVoteKind.Passage, "On Motion to Concur in the Senate Amendment", "Vote to pass or agree to (On Motion to Concur in the Senate Amendment)")]
    [InlineData(PublishedVoteKind.MotionToTable, "On Motion to Table", "Vote to set aside and reject (On Motion to Table)")]
    [InlineData(PublishedVoteKind.MotionToDischarge, "On Motion to Discharge", "Vote to pull from committee (On Motion to Discharge)")]
    [InlineData(PublishedVoteKind.Cloture, "On the Cloture Motion", "Vote to end debate (On the Cloture Motion)")]
    [InlineData(PublishedVoteKind.Nomination, "On the Nomination", "Vote to confirm (On the Nomination)")]
    [InlineData(PublishedVoteKind.Other, "On Motion to Adjourn", "On Motion to Adjourn")]
    public void AQuestionShowsThePlainKindNameFirstAndTheSourceQuestionInBracketsAndKindOtherShowsTheSourceAlone(PublishedVoteKind kind, string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Question(kind, source));

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("passed", "Passed")]
    [InlineData("Failed", "Failed")]
    [InlineData("Agreed to", "Passed")]
    [InlineData("Cloture Motion Agreed to", "Passed")]
    [InlineData("Bill Passed", "Passed")]
    [InlineData("Confirmed", "Confirmed")]
    [InlineData("Nomination Confirmed", "Confirmed")]
    [InlineData("Nomination Rejected", "Not confirmed")]
    [InlineData("Nomination Not Confirmed", "Not confirmed")]
    [InlineData("Not Agreed to", "Failed")]
    [InlineData("Rejected", "Failed")]
    [InlineData("Amendment Rejected", "Failed")]
    [InlineData("Cloture Motion Rejected", "Failed")]
    [InlineData("  Passed ", "Passed")]
    public void AResultShowsOnePlainWordAndNeverRepeatsItWithTheSourceWords(string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.Result(source));

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("Agreed to", "Passed (Agreed to)")]
    [InlineData("Bill Passed", "Passed (Bill Passed)")]
    [InlineData("Nomination Confirmed", "Confirmed (Nomination Confirmed)")]
    [InlineData("Amendment Rejected", "Failed (Amendment Rejected)")]
    [InlineData("  Passed ", "Passed")]
    public void TheResultFactOfTheSourceFoldShowsThePlainWordsFirstAndTheSourceWordsInBrackets(string source, string expected) =>
        Assert.Equal(expected, PlainVoteWords.ResultWithSource(source));

    [Theory]
    [InlineData("Point of Order Sustained")]
    [InlineData("Not Passed")]
    [InlineData("Unpassed")]
    [InlineData("Passed <b>")]
    public void AnUnknownResultShowsTheSourceWordsAlone(string source)
    {
        Assert.Equal(source, PlainVoteWords.Result(source));
        Assert.Equal(source, PlainVoteWords.ResultWithSource(source));
    }

    [Theory]
    [InlineData("Bill Passed", true)]
    [InlineData("Passed", true)]
    [InlineData("Agreed to", true)]
    [InlineData("Nomination Confirmed", true)]
    [InlineData("Confirmed", true)]
    [InlineData("Failed", false)]
    [InlineData("Amendment Rejected", false)]
    [InlineData("Not Agreed to", false)]
    [InlineData("Nomination Rejected", false)]
    [InlineData("Not confirmed", false)]
    [InlineData("Point of Order Sustained", null)]
    public void AFailedResultNeverReadsAsCarriedInTheSourceWordsOrThePlainWord(string result, bool? carried)
    {
        Assert.Equal(carried, PlainVoteWords.Carried(result));
        Assert.Equal(carried, PlainVoteWords.Carried(PlainVoteWords.Result(result)));
    }

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
