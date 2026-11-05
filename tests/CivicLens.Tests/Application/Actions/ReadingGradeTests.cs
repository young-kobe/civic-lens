using CivicLens.Application.Actions;

namespace CivicLens.Tests.Application.Actions;

public sealed class ReadingGradeTests
{
    [Fact]
    public void TheGradeFollowsTheFleschKincaidFormulaWithOneSentenceForEachItem()
    {
        // 4 words, 4 syllables: 0.39 * 4 + 11.8 * 1 - 15.59 = -2.23
        Assert.Equal(-2.23, ReadingGrade.Of("The cat sat down."), 2);
        // 3 words, 10 syllables (re-spon-si-ble 4, de-part-ment 3, of-fi-cials 3): 0.39 * 3 + 11.8 * (8 / 3) - 15.59
        Assert.Equal(0.39 * 3 + 11.8 * 10 / 3 - 15.59, ReadingGrade.Of("responsible department officials"), 5);
    }

    [Theory]
    [InlineData("cat", 1)]
    [InlineData("income", 2)]
    [InlineData("the", 1)]
    [InlineData("table", 2)]
    [InlineData("Secretary", 4)]
    [InlineData("2030", 1)]
    public void ASyllableIsAGroupOfVowelsLessAFinalSilentE(string word, int syllables) =>
        Assert.Equal(0.39 + 11.8 * syllables - 15.59, ReadingGrade.Of(word), 5);

    [Fact]
    public void AFullStopInAnAbbreviationDoesNotSplitTheSentence() =>
        Assert.Equal(0.39 * 4 + 11.8 * 4 / 4 - 15.59, ReadingGrade.Of("H.R. 764 pays rent."), 5);

    [Fact]
    public void LongLegalWordsPassOnlyBelowTheLimitAndPlainWordsPass()
    {
        Assert.True(ReadingGrade.Of("Notwithstanding the aforementioned administrative requirements, eligible beneficiaries receive compensatory disbursements.") >
            ReadingGrade.MaximumSentenceGrade);
        Assert.True(ReadingGrade.Of("The bill pays rent for people with low pay.") <= ReadingGrade.MaximumSentenceGrade);
        Assert.Equal("8.0", ReadingGrade.Format(ReadingGrade.MaximumSentenceGrade));
    }
}
