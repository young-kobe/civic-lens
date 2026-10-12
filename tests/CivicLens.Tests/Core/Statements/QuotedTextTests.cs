using CivicLens.Core.Statements;

namespace CivicLens.Tests.Core.Statements;

public sealed class QuotedTextTests
{
    [Theory]
    [InlineData("He said \"we will act now\" today.", "we will act now", StatementQuotationKind.DirectQuotation)]
    [InlineData("He said “we will act now” today.", "we will act now", StatementQuotationKind.DirectQuotation)]
    [InlineData("He said \"we will act now\" today.", "He said", StatementQuotationKind.ReleaseText)]
    [InlineData("He said \"we will act now\" today.", "\"we will act now\"", StatementQuotationKind.ReleaseText)]
    [InlineData("He said \"we will act now\" today.", "act now\" today", StatementQuotationKind.ReleaseText)]
    [InlineData("\"one\" and \"two\"", "and", StatementQuotationKind.ReleaseText)]
    [InlineData("\"one\" and \"two\"", "two", StatementQuotationKind.DirectQuotation)]
    [InlineData("It's the senator's \"plan\" now", "senator's", StatementQuotationKind.ReleaseText)]
    [InlineData("a” b “c” d", "b", StatementQuotationKind.ReleaseText)]
    [InlineData("a” b “c” d", "c", StatementQuotationKind.DirectQuotation)]
    public void ASpanIsADirectQuotationOnlyWhenItLiesEntirelyInsideOneQuotedPassage(string text, string fragment, StatementQuotationKind expected) =>
        Assert.Equal(expected, Kind(text, fragment));

    [Fact]
    public void AQuotationWithNoClosingMarkRunsToTheEndOfItsLineSoAParagraphThatContinuesTheQuotationCounts()
    {
        const string text = "\"The first paragraph does not close.\n\"The second paragraph closes it.\"\nThe release resumes.";

        Assert.Equal(StatementQuotationKind.DirectQuotation, Kind(text, "The first paragraph does not close."));
        Assert.Equal(StatementQuotationKind.DirectQuotation, Kind(text, "The second paragraph closes it."));
        Assert.Equal(StatementQuotationKind.ReleaseText, Kind(text, "The release resumes."));
    }

    [Fact]
    public void AStrayOpeningMarkNeverLeaksIntoTheNextLine() =>
        Assert.Equal(StatementQuotationKind.ReleaseText, Kind("He said \"stop\nThen he left.", "Then he left."));

    [Fact]
    public void CurlyQuotationsDoNotNestSoAnInnerOpeningMarkIsPlainText()
    {
        const string text = "“outer “inner” more”";

        Assert.Equal(StatementQuotationKind.DirectQuotation, Kind(text, "outer “inner"));
        Assert.Equal(StatementQuotationKind.ReleaseText, Kind(text, "more"));
    }

    [Fact]
    public void ASpanMustHaveALength() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuotedText("text").KindOf(0, 0));

    private static StatementQuotationKind Kind(string text, string fragment) =>
        new QuotedText(text).KindOf(text.IndexOf(fragment, StringComparison.Ordinal), fragment.Length);
}
