using System.Text;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Search;

/// <summary>
/// The one owner of the snippet cut. A snippet is the start of the snippet text of a record: its published summary or its bill line, never a source text.
/// The text starts a sentence, so the snippet never starts mid-sentence or mid-word. A text of at most <see cref="MaximumCharacters"/> characters stays whole.
/// A longer text ends after its last whole sentence within the limit, where <see cref="SentenceEnds"/> says. With no such sentence, it ends at the last
/// white space before the limit, drops a trailing comma, semicolon, or colon, and takes the cut marker. The limit counts the text, not the match marks.
/// A cut inside a match closes its mark.
/// </summary>
public static class SearchSnippet
{
    public const int MaximumCharacters = 240;

    /// <summary>The characters of the snippet text that the index marks. A sentence end at the limit needs the next word to be seen.</summary>
    public const int ScanCharacters = 1_000;

    private const string CutMarker = "…";

    public static string Cut(string marked)
    {
        var trimmed = marked.Trim();
        var (text, rawEnds) = Visible(trimmed);
        if (text.Length <= MaximumCharacters) return trimmed;
        var sentence = SentenceEnds.After(text).TakeWhile(end => end <= MaximumCharacters).LastOrDefault();
        if (sentence > 0) return Prefix(trimmed, rawEnds, sentence);
        var words = WordCut(text);
        return Prefix(trimmed, rawEnds, words > 0 ? words : HardCut(text)) + CutMarker;
    }

    // The visible text without marks, and for each visible length the raw length that holds it.
    private static (string Text, int[] RawEnds) Visible(string marked)
    {
        var text = new StringBuilder(marked.Length);
        var rawEnds = new List<int>(marked.Length + 1) { 0 };
        for (var index = 0; index < marked.Length; index++)
        {
            if (marked[index] is SearchResultsPage.MarkStart or SearchResultsPage.MarkEnd) continue;
            text.Append(marked[index]);
            rawEnds.Add(index + 1);
        }
        return (text.ToString(), [.. rawEnds]);
    }

    private static int WordCut(string text)
    {
        for (var index = MaximumCharacters - CutMarker.Length; index > 0; index--)
        {
            if (!char.IsWhiteSpace(text[index])) continue;
            var end = text[..index].TrimEnd().TrimEnd(',', ';', ':').TrimEnd().Length;
            if (end > 0) return end;
        }
        return 0;
    }

    private static int HardCut(string text)
    {
        var end = MaximumCharacters - CutMarker.Length;
        return char.IsHighSurrogate(text[end - 1]) ? end - 1 : end;
    }

    // The raw text up to the given visible length. A mark that opens before the cut and does not close gets its end mark.
    private static string Prefix(string marked, int[] rawEnds, int visibleLength)
    {
        var raw = marked[..rawEnds[visibleLength]];
        var open = raw.LastIndexOf(SearchResultsPage.MarkStart) > raw.LastIndexOf(SearchResultsPage.MarkEnd);
        return open ? raw + SearchResultsPage.MarkEnd : raw;
    }
}
