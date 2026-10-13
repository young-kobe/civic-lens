using CivicLens.Application.Statements;
using CivicLens.Core.Statements;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementSelectableSpansTests
{
    private const string Quote = "a rent cap for tenants";

    private static StatementDraftRevision Saved(StatementSpan quotation, StatementSpan? dateEvidence = null) =>
        new(new string('a', 64), 2, "auth0|reviewer", DateTimeOffset.UtcNow, "h", "s", dateEvidence is null ? null : new DateOnly(2026, 10, 3),
            dateEvidence, [], [], [], [], [new(quotation, StatementQuotationKind.ReleaseText)], []);

    [Fact]
    public void SavedSpansComeFirstThenQuotedPassagesThenParagraphsWithoutRepeatingASpan()
    {
        var extraction = StatementGateFixtures.Evidence(StatementDraftFixtures.Article).Extraction;
        var text = extraction.Text;
        var quote = new StatementSpan(extraction.ExtractionId, text.IndexOf(Quote, StringComparison.Ordinal), Quote.Length);
        var firstParagraph = new StatementSpan(extraction.ExtractionId, 0, text.IndexOf('\n'));

        var options = StatementSelectableSpans.Build(extraction, Saved(quote, firstParagraph));

        Assert.Equal([StatementSpanOrigin.Saved, StatementSpanOrigin.Saved], options.Take(2).Select(option => option.Origin));
        Assert.Equal([Quote, text[..firstParagraph.Length]], options.Take(2).Select(option => option.Text));
        Assert.Equal(["This cap protects families,", "We welcome it."],
            options.Where(option => option.Origin == StatementSpanOrigin.QuotedPassage).Select(option => option.Text));
        Assert.Equal(2, options.Count(option => option.Origin == StatementSpanOrigin.Paragraph));
        Assert.Equal(options.Length, options.Select(option => option.Key).Distinct().Count());
    }

    [Fact]
    public void ADraftWithoutARevisionOffersQuotedPassagesAndParagraphsOnly()
    {
        var extraction = StatementGateFixtures.Evidence(StatementDraftFixtures.Article).Extraction;

        var options = StatementSelectableSpans.Build(extraction, null);

        Assert.DoesNotContain(options, option => option.Origin == StatementSpanOrigin.Saved);
        Assert.Equal(StatementSpanOrigin.QuotedPassage, options[0].Origin);
    }

    [Fact]
    public void TheListStopsAtTheMaximumSoALongArticleCannotFloodTheForm()
    {
        var extraction = StatementGateFixtures.Evidence(string.Join('\n', Enumerable.Range(0, 500).Select(index => $"Paragraph {index}."))).Extraction;

        var options = StatementSelectableSpans.Build(extraction, null);

        Assert.Equal(StatementSelectableSpans.Maximum, options.Length);
        Assert.Equal("Paragraph 0.", options[0].Text);
    }

    [Fact]
    public void AParagraphLongerThanAQuotationCanBeIsNotOffered()
    {
        var tooLong = new string('x', StatementDraftRevision.MaximumQuotationLength + 1);
        var extraction = StatementGateFixtures.Evidence($"Short paragraph.\n{tooLong}\n").Extraction;

        var options = StatementSelectableSpans.Build(extraction, null);

        Assert.Equal(["Short paragraph."], options.Select(option => option.Text));
    }
}
