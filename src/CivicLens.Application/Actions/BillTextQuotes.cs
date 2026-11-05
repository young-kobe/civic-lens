using System.Text;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of how the model reads the bill text and of how a quote is found in it. GPO prints a double quote mark as two
/// grave accents (``) or two apostrophes (''). <see cref="Normalize"/> turns each pair into one plain double quote. The prompt sends the
/// normalized body, and <see cref="Find"/> searches the same normalized body. A found quote maps back to a span of the stored text, so the
/// citation still names the exact characters of the extraction.</summary>
public sealed class BillTextQuotes
{
    private const string PlainQuote = "\"";
    private readonly ActionDraftBillText billText;
    private readonly string normalizedBody;

    public BillTextQuotes(ActionDraftBillText billText)
    {
        ArgumentNullException.ThrowIfNull(billText);
        this.billText = billText;
        normalizedBody = Normalize(billText.Body);
    }

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            var length = MarkLength(text, index);
            builder.Append(length == 2 ? PlainQuote : text[index].ToString());
            index += length;
        }
        return builder.ToString();
    }

    /// <summary>The span of an exact quote of the normalized bill text, in the stored text. It is null when the characters read do not hold the quote.</summary>
    public SourceSpan? Find(string quote)
    {
        if (quote.Length == 0) return null;
        var normalizedStart = normalizedBody.IndexOf(quote, StringComparison.Ordinal);
        if (normalizedStart < 0) return null;
        var start = billText.BodyStart + StoredOffset(normalizedStart);
        var end = billText.BodyStart + StoredOffset(normalizedStart + quote.Length);
        var readEnd = billText.BodyStart + billText.CharsRead;
        return end > readEnd || SplitsSurrogatePair(billText.Text, start) || SplitsSurrogatePair(billText.Text, end)
            ? null : new(billText.ExtractionId, start, end - start);
    }

    private int StoredOffset(int normalizedOffset)
    {
        var body = billText.Body;
        var stored = 0;
        for (var normalized = 0; normalized < normalizedOffset; normalized++) stored += MarkLength(body, stored);
        return stored;
    }

    private static int MarkLength(string text, int index)
    {
        if (index + 1 >= text.Length || text[index] != text[index + 1]) return 1;
        return text[index] is '`' or '\'' ? 2 : 1;
    }

    private static bool SplitsSurrogatePair(string text, int boundary) => boundary > 0 && boundary < text.Length &&
        char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]);
}
