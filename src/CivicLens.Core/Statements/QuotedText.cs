namespace CivicLens.Core.Statements;

/// <summary>
/// Finds the passages that sit inside double quotation marks. A straight mark toggles. A curly opening mark opens and a curly closing mark
/// closes. A mark of the wrong kind for the current state is plain text. A quotation does not cross a line break: an opening mark with no
/// closing mark on its line runs to the end of the line, which is how a quotation that continues into the next paragraph is written.
/// </summary>
public sealed class QuotedText
{
    private const char StraightMark = '"';
    private const char CurlyOpen = '“';
    private const char CurlyClose = '”';
    private readonly List<(int Start, int End)> regions = [];

    public QuotedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var openedAt = -1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\n' or '\r') openedAt = Close(openedAt, index);
            else if (openedAt < 0 && character is StraightMark or CurlyOpen) openedAt = index + 1;
            else if (openedAt >= 0 && character is StraightMark or CurlyClose) openedAt = Close(openedAt, index);
        }
        Close(openedAt, text.Length);
    }

    public StatementQuotationKind KindOf(int start, int length)
    {
        if (start < 0 || length < 1) throw new ArgumentOutOfRangeException(nameof(start));
        return regions.Any(region => region.Start <= start && start + length <= region.End)
            ? StatementQuotationKind.DirectQuotation : StatementQuotationKind.ReleaseText;
    }

    private int Close(int openedAt, int end)
    {
        if (openedAt >= 0 && end > openedAt) regions.Add((openedAt, end));
        return -1;
    }
}
