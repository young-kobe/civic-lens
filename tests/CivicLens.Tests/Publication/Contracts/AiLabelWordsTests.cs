using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class AiLabelWordsTests
{
    [Fact]
    public void TheApprovedLabelsStayAsTheOwnerWroteThemBecauseTheViewsRepeatThemUnderComparisonTests()
    {
        Assert.Equal("An AI summary of the official record", AiLabelWords.ModelWording);
        Assert.Equal("Written automatically from Congress.gov data, with no AI", AiLabelWords.CodeWording);
        Assert.Equal("An AI summary of this release.", AiLabelWords.StatementModelWording);
        Assert.Equal("Drafted by an AI model from this source. A person checked it before publication.", AiLabelWords.StatementCheckedByPersonWording);
        Assert.Equal("A model wrote only this summary.", AiLabelWords.ModelWroteOnlyThisSummary);
    }
}
