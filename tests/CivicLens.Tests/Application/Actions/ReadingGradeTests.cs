using CivicLens.Application.Actions;

namespace CivicLens.Tests.Application.Actions;

public sealed class ReadingGradeTests
{
    [Fact]
    public void TheGradeFollowsTheFleschKincaidFormulaWithOneSentence()
    {
        // 4 words, 4 syllables: 0.39 * 4 + 11.8 * 1 - 15.59 = -2.23
        Assert.Equal(-2.23, ReadingGrade.Of("The cat sat down."), 2);
        // 3 words, 10 syllables (re-spon-si-ble 4, de-part-ment 3, of-fi-cials 3): 0.39 * 3 + 11.8 * (10 / 3) - 15.59
        Assert.Equal(0.39 * 3 + 11.8 * 10 / 3 - 15.59, ReadingGrade.Of("responsible department officials"), 5);
    }

    [Theory]
    [InlineData("cat", 1)]
    [InlineData("income", 2)]
    [InlineData("the", 1)]
    [InlineData("table", 2)]
    [InlineData("Secretary", 4)]
    [InlineData("2030", 1)]
    [InlineData("states", 1)]
    [InlineData("requires", 2)]
    [InlineData("used", 1)]
    [InlineData("needed", 2)]
    [InlineData("taxes", 2)]
    [InlineData("passed", 1)]
    [InlineData("makes", 1)]
    [InlineData("reaches", 2)]
    [InlineData("wished", 1)]
    [InlineData("wishes", 2)]
    [InlineData("wanted", 2)]
    public void ASyllableIsAGroupOfVowelsLessAFinalSilentEndingEAnEsOrAnEd(string word, int syllables) =>
        Assert.Equal(0.39 + 11.8 * syllables - 15.59, ReadingGrade.Of(word), 5);

    [Theory]
    [InlineData("H.R. 764 pays rent.", "HR 764 pays rent.")]
    [InlineData("The U.S. Congress pays rent.", "The US Congress pays rent.")]
    [InlineData("It amends 42 U.S.C. Section 5 for D.C. Offices.", "It amends 42 USC Section 5 for DC Offices.")]
    [InlineData("See Pub. L. Number 5 for rent.", "See Pub L Number 5 for rent.")]
    public void AFullStopInAnAbbreviationDoesNotSplitTheSentence(string abbreviated, string plain) =>
        Assert.Equal(ReadingGrade.Of(plain), ReadingGrade.Of(abbreviated), 5);

    [Fact]
    public void TwoShortSentencesInOneItemGradeAsTwoSentences()
    {
        // 6 words in 2 sentences, 6 syllables: 0.39 * 3 + 11.8 - 15.59
        Assert.Equal(0.39 * 3 + 11.8 - 15.59, ReadingGrade.Of("The cat sat. The dog ran."), 5);
        Assert.Equal(0.39 * 3 + 11.8 - 15.59, ReadingGrade.Of("The cat sat? The dog ran!"), 5);
    }

    [Fact]
    public void TheRoundedGradeIsTheValueThatTheLimitAndTheMessageUse()
    {
        const string text = "Many tenants pay rent into local landlords under the new program.";

        Assert.True(ReadingGrade.Of(text) > ReadingGrade.MaximumSentenceGrade);
        Assert.Equal(ReadingGrade.MaximumSentenceGrade, ReadingGrade.Rounded(text));
        Assert.Equal("8.0", ReadingGrade.Format(ReadingGrade.Rounded(text)));
    }

    [Fact]
    public void LongLegalWordsPassOnlyBelowTheLimitAndPlainWordsPass()
    {
        Assert.True(ReadingGrade.Of("Notwithstanding the aforementioned administrative requirements, eligible beneficiaries receive compensatory disbursements.") >
            ReadingGrade.MaximumSentenceGrade);
        Assert.True(ReadingGrade.Of("The bill pays rent for people with low pay.") <= ReadingGrade.MaximumSentenceGrade);
        Assert.Equal("8.0", ReadingGrade.Format(ReadingGrade.MaximumSentenceGrade));
    }
}
