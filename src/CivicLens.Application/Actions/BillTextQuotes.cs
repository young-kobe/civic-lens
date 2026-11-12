using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of how the model reads the bill text and of how a quote is found in it. GPO prints a double quote mark as two
/// grave accents (``) or two apostrophes (''). <see cref="GpoQuoteMarks.Normalize"/> turns each pair into one plain double quote. The prompt sends the
/// normalized body, and <see cref="Find"/> searches the same normalized body. A found quote maps back to a span of the stored text, so the
/// citation still names the exact characters of the extraction.</summary>
public sealed class BillTextQuotes
{
    private readonly ActionDraftBillText billText;
    private readonly string normalizedBody;

    public BillTextQuotes(ActionDraftBillText billText)
    {
        ArgumentNullException.ThrowIfNull(billText);
        this.billText = billText;
        normalizedBody = GpoQuoteMarks.Normalize(billText.Body);
    }

    /// <summary>The body of the bill text as the prompt sends it.</summary>
    public string NormalizedBody => normalizedBody;

    /// <summary>The span of an exact quote of the normalized bill text, in the stored text. It is null when the characters read do not hold the quote.</summary>
    public SourceSpan? Find(string quote) => FindIn(quote, 0, billText.Body.Length, normalizedBody);

    /// <summary>The span of an exact quote that lies inside the definitions section. A quote that the section does not hold is null, even when
    /// another section holds the same words.</summary>
    public SourceSpan? FindInDefinitions(string quote, BillDefinitionsSection section) => FindIn(quote, section.Start, section.End, null);

    /// <summary>Searches the normalized form of one range of the body. The whole-body caller passes the normalized body it already holds.</summary>
    private SourceSpan? FindIn(string quote, int bodyStart, int bodyEnd, string? normalizedRange)
    {
        if (quote.Length == 0) return null;
        var stored = billText.Body[bodyStart..bodyEnd];
        var normalizedStart = (normalizedRange ?? GpoQuoteMarks.Normalize(stored)).IndexOf(quote, StringComparison.Ordinal);
        if (normalizedStart < 0) return null;
        var start = billText.BodyStart + bodyStart + StoredOffset(stored, normalizedStart);
        var end = billText.BodyStart + bodyStart + StoredOffset(stored, normalizedStart + quote.Length);
        var readEnd = billText.BodyStart + billText.CharsRead;
        return end > readEnd || SplitsSurrogatePair(billText.Text, start) || SplitsSurrogatePair(billText.Text, end)
            ? null : new(billText.ExtractionId, start, end - start);
    }

    private static int StoredOffset(string body, int normalizedOffset)
    {
        var stored = 0;
        for (var normalized = 0; normalized < normalizedOffset; normalized++) stored += GpoQuoteMarks.MarkLength(body, stored);
        return stored;
    }

    private static bool SplitsSurrogatePair(string text, int boundary) => boundary > 0 && boundary < text.Length &&
        char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]);
}
