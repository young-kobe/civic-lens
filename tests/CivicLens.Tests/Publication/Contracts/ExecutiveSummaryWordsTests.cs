using CivicLens.Application.Collection;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class ExecutiveSummaryWordsTests
{
    [Fact]
    public void TheLabelAndTheCutNoteKeepTheProposedWordsUntilTheOwnerChangesThem()
    {
        Assert.Equal("AI summary of the document text. An AI model wrote this. Code checked that each sentence quotes the text.", ExecutiveSummaryWords.Label);
        Assert.Equal("The AI read only the first 60,000 characters of this document. The rest is not summarized.", ExecutiveSummaryWords.CutNote);
    }

    [Fact]
    public void TheCutNoteNamesTheWindowThatTheTextExtractionKeepsBecauseTheContractHasNoDependency()
    {
        Assert.Equal(TextWindow.Length, ExecutiveSummaryWords.WindowLength);
    }
}
