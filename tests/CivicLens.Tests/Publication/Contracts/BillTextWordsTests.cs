using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class BillTextWordsTests
{
    [Fact]
    public void TheApprovedWordsStayAsTheOwnerWroteThemBecauseAPageNeverReadsAChangedWording()
    {
        Assert.Equal("AI summary of the bill text. An AI model wrote this. Code checked that each sentence quotes the text.", BillTextWords.Label);
        Assert.Equal("Why it matters, as the text states it.", BillTextWords.WhyItMattersHeading);
    }

    [Fact]
    public void TheSourceLineNamesTheVersionAndItsDateAndASourceWithNoDateShowsWithoutOne()
    {
        Assert.Equal("Based on: Introduced in House, 3 Jan 2026. Read the full text on Congress.gov.", BillTextWords.SourceLine("Introduced in House", "3 Jan 2026"));
        Assert.Equal("Based on: Introduced in House. Read the full text on Congress.gov.", BillTextWords.SourceLine("Introduced in House", null));
    }

    [Fact]
    public void TheCutNoteCountsTheCharactersReadAndAllCharactersAndWritesNothingForATextReadInFull()
    {
        Assert.Equal("This summary covers the first 59,987 of 85,000 characters. Later sections are not covered.", BillTextWords.CutNote(59_987, 85_000));
        Assert.Null(BillTextWords.CutNote(40_000, 40_000));
    }
}
