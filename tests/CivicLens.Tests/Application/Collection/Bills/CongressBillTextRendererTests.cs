using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillTextRendererTests
{
    private const int Window = CongressBillRenderer.TextWindowLength;
    private static readonly CongressBillListItem Bill = new(119, "hr", 7, default);
    private static readonly CongressBillTextVersion Introduced = new("BILLS-119hr7ih", "Introduced in House",
        new DateTimeOffset(2025, 3, 1, 5, 0, 0, TimeSpan.Zero), "https://www.congress.gov/119/bills/hr7/BILLS-119hr7ih.htm");

    private static string Lines(int count, int width) =>
        string.Join('\n', Enumerable.Range(0, count).Select(index => index.ToString("D6") + new string('x', width - 6)));

    [Fact]
    public void ShortTextIsKeptWholeWithAHeaderThatNamesBillVersionDateAndCounts()
    {
        var reading = CongressBillRenderer.RenderText(Bill, Introduced, "Section 1.\nShort title.");

        Assert.Equal("""
            Bill: 119 hr 7
            Version: Introduced in House
            Version date: 2025-03-01
            Package: BILLS-119hr7ih
            Characters read: 23 of 23
            Text:
            Section 1.
            Short title.
            """.ReplaceLineEndings("\n"), reading.Text);
        Assert.Equal((23, 23), (reading.CharsRead, reading.CharsTotal));
    }

    [Fact]
    public void TextOfExactlyTheWindowIsNotCut()
    {
        var text = new string('a', Window);

        var reading = CongressBillRenderer.RenderText(Bill, Introduced, text);

        Assert.Equal((Window, Window), (reading.CharsRead, reading.CharsTotal));
        Assert.EndsWith("\n" + text, reading.Text);
    }

    [Fact]
    public void LongTextIsCutAtTheLastLineBreakInsideTheWindowAndTheHeaderStatesTheHonestCounts()
    {
        var text = Lines(count: 1_000, width: 100);

        var reading = CongressBillRenderer.RenderText(Bill, Introduced, text);

        var window = reading.Text[(reading.Text.IndexOf("Text:\n", StringComparison.Ordinal) + "Text:\n".Length)..];
        Assert.True(reading.CharsRead is > Window - 101 and <= Window);
        Assert.Equal(text.Length, reading.CharsTotal);
        Assert.Equal(reading.CharsRead, window.Length);
        Assert.StartsWith(window, text);
        Assert.Equal('x', window[^1]);
        Assert.Equal('\n', text[window.Length]);
        Assert.Contains($"Characters read: {reading.CharsRead} of {text.Length}\n", reading.Text);
    }

    [Fact]
    public void ALineBreakExactlyAtTheEndOfTheWindowKeepsTheWholeWindow()
    {
        var text = new string('a', Window) + "\n" + new string('b', 50);

        var reading = CongressBillRenderer.RenderText(Bill, Introduced, text);

        Assert.Equal((Window, Window + 51), (reading.CharsRead, reading.CharsTotal));
    }

    [Fact]
    public void FlatTextFromTheExtractorIsCutAtTheLastSpaceInsideTheWindowSoNoWordIsCut()
    {
        var text = string.Join(' ', Enumerable.Range(0, 20_000).Select(index => "word" + index));

        var reading = CongressBillRenderer.RenderText(Bill, Introduced, text);

        Assert.True(reading.CharsRead <= Window && text[reading.CharsRead] == ' ');
        Assert.Equal(text.Length, reading.CharsTotal);
        Assert.EndsWith(text[..reading.CharsRead], reading.Text);
    }

    [Fact]
    public void LongTextWithNoWhitespaceInsideTheWindowIsInvalidInsteadOfCutThroughAWord() =>
        Assert.Throws<InvalidDataException>(() =>
            CongressBillRenderer.RenderText(Bill, Introduced, new string('a', Window + 1)));

    [Fact]
    public void EmptyTextIsInvalidBecauseAnEmptyRowCouldNeverBeSummarized() =>
        Assert.Throws<InvalidDataException>(() => CongressBillRenderer.RenderText(Bill, Introduced, ""));

    [Fact]
    public void AVersionWithNoDateSaysSoInTheHeader()
    {
        var reading = CongressBillRenderer.RenderText(Bill, Introduced with { Date = null }, "Text.");

        Assert.Contains("\nVersion date: not given\n", reading.Text);
    }
}
