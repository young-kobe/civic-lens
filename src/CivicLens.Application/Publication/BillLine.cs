using System.Text.RegularExpressions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the one line that says what a bill does. Code takes the first sentence of the Congressional Research Service summary
/// and caps it at <see cref="PublishedRecordEntry.MaximumBillLineLength"/> characters at a word boundary. No other text becomes the line.
/// </summary>
internal static partial class BillLine
{
    private const string SummaryTextMarker = "Text:";

    public static PublishedRecordEntry EntryOf(PublishedAction action) => action.ToEntry() with { BillLine = Of(action) };

    public static string? Of(PublishedAction action) => action.SummaryCitation is { } summary ? FromSummaryQuote(summary.Quote) : null;

    private static string? FromSummaryQuote(string quote)
    {
        var lines = quote.Split('\n');
        var marker = Array.IndexOf(lines, SummaryTextMarker);
        if (marker < 0) return null;
        var paragraph = lines[(marker + 1)..].Select(line => line.Trim()).FirstOrDefault(EndsSentence);
        return paragraph is null ? null : Cap(FirstSentence(paragraph));
    }

    private static bool EndsSentence(string line) => line.Length > 0 && line[^1] is '.' or '!' or '?';

    private static string FirstSentence(string paragraph)
    {
        var end = SentenceEnd().Match(paragraph);
        return end.Success ? paragraph[..end.Length].TrimEnd() : paragraph;
    }

    private static string Cap(string sentence)
    {
        if (sentence.Length <= PublishedRecordEntry.MaximumBillLineLength) return sentence;
        var space = sentence.LastIndexOf(' ', PublishedRecordEntry.MaximumBillLineLength);
        var length = space > 0 ? space : PublishedRecordEntry.MaximumBillLineLength;
        return sentence[..length].TrimEnd(' ', ',', ';', ':');
    }

    [GeneratedRegex(@"^.+?[.!?](?=\s+[\p{Lu}""“(]|$)", RegexOptions.Singleline)]
    private static partial Regex SentenceEnd();
}
