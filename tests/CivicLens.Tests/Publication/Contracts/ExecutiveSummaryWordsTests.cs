using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class ExecutiveSummaryWordsTests
{
    [Fact]
    public void TheLabelAndTheCutNoteKeepTheWordsTheOwnerDecided()
    {
        Assert.Equal("An AI model summarized the document text. Code checks each quote.", ExecutiveSummaryWords.Label);
        Assert.Equal("This summary covers only the first part of the document.", ExecutiveSummaryWords.CutNote);
    }
}
