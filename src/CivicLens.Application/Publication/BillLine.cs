using System.Text.RegularExpressions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the one line that says what a bill does. Code takes the first sentence of the Congressional Research Service summary
/// and caps it at <see cref="PublishedRecordEntry.MaximumBillLineLength"/> characters at a word boundary, with a cut marker.
/// No other text becomes the line.
/// </summary>
internal static partial class BillLine
{
    private const string CutMarker = "…";

    public static PublishedRecordEntry EntryOf(PublishedAction action) => action.ToEntry() with { BillLine = Of(action) };

    private static string? Of(PublishedAction action) => action.SummaryCitation is { } summary ? FromSummaryQuote(summary.Quote) : null;

    /// <summary>The line of one summary block of the bill summaries page, as the vote drafting reads it.</summary>
    internal static string? FromSummaryQuote(string quote)
    {
        var lines = quote.Split('\n');
        var marker = Array.IndexOf(lines, CongressBillRenderer.SummaryTextLabel);
        return marker < 0 ? null : FromParagraphs(lines[(marker + 1)..].Select(line => line.Trim()));
    }

    private static string? FromParagraphs(IEnumerable<string> paragraphs)
    {
        foreach (var paragraph in paragraphs.Where(paragraph => paragraph.Length > 0))
        {
            if (EndsSentence(paragraph)) return Cap(FirstSentence(paragraph));
            if (!IsTitle(paragraph)) return null;
        }
        return null;
    }

    private static bool EndsSentence(string paragraph) => FinalMark().IsMatch(paragraph);

    private static bool IsTitle(string paragraph) =>
        paragraph.Length <= PublishedRecordEntry.MaximumBillLineLength && (char.IsLetterOrDigit(paragraph[^1]) || paragraph[^1] == ')');

    private static string FirstSentence(string paragraph) =>
        SentenceEnds.After(paragraph).Cast<int?>().FirstOrDefault() is { } end ? paragraph[..end] : paragraph;

    private static string? Cap(string sentence)
    {
        if (sentence.Length <= PublishedRecordEntry.MaximumBillLineLength) return sentence;
        var boundary = LastWhitespaceBefore(sentence, PublishedRecordEntry.MaximumBillLineLength - CutMarker.Length + 1);
        var text = sentence[..boundary].TrimEnd().TrimEnd(',', ';', ':').TrimEnd();
        return boundary > 0 && text.Length > 0 ? text + CutMarker : null;
    }

    private static int LastWhitespaceBefore(string text, int end)
    {
        for (var index = end - 1; index > 0; index--)
            if (char.IsWhiteSpace(text[index])) return index;
        return 0;
    }

    [GeneratedRegex("[.!?][" + SentenceEnds.ClosingCharacters + "]*$")]
    private static partial Regex FinalMark();
}
