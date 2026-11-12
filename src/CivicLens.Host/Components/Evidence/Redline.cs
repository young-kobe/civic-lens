using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Components.Evidence;

public static class Redline
{
    /// <summary>Builds the redline of one hunk. A hunk holds its changed text and its context windows, not the whole documents. The public builder reads offsets into whole texts, so the hunk becomes one small text on each side: the leading context and the removed text, then the added text and the trailing context.</summary>
    public static IReadOnlyList<RedlineSegment> Build(DocumentComparisonHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        var leading = LeadingContext(hunk);
        var trailing = TrailingContext(hunk);
        var change = new PublishedChange
        {
            BeforeStart = leading.Length,
            BeforeLength = hunk.BeforeText.Length,
            AfterStart = 0,
            AfterLength = hunk.AfterText.Length,
            BeforeContextStart = 0,
            BeforeContextLength = leading.Length + hunk.BeforeText.Length,
            AfterContextStart = 0,
            AfterContextLength = hunk.AfterText.Length + trailing.Length,
            WordEdits = [.. hunk.WordEdits.Select(edit => new PublishedWordEdit
            {
                BeforeStart = leading.Length + edit.BeforeStart - hunk.BeforeStart,
                BeforeLength = edit.BeforeLength,
                AfterStart = edit.AfterStart - hunk.AfterStart,
                AfterLength = edit.AfterLength
            })]
        };
        return DocumentChangeRedline.Build(change, leading + hunk.BeforeText, hunk.AfterText + trailing);
    }

    public static IReadOnlyList<RedlineSegment> Excerpt(DocumentComparisonHunk hunk, int surroundingLength)
    {
        var change = Build(hunk).Where(segment => segment.Kind != RedlineKind.Context).ToList();
        var first = change.FindIndex(segment => segment.Kind != RedlineKind.Same);
        var last = change.FindLastIndex(segment => segment.Kind != RedlineKind.Same);
        if (first < 0) return [];
        var excerpt = new List<RedlineSegment>();
        if (first > 0) Add(excerpt, RedlineKind.Same, Tail(change[first - 1].Text, surroundingLength));
        excerpt.AddRange(change.Skip(first).Take(last - first + 1));
        if (last + 1 < change.Count) Add(excerpt, RedlineKind.Same, Head(change[last + 1].Text, surroundingLength));
        return excerpt;
    }

    private static string LeadingContext(DocumentComparisonHunk hunk)
    {
        var length = hunk.BeforeStart - hunk.BeforeContextStart;
        return length > 0 && length <= hunk.BeforeContext.Length ? hunk.BeforeContext[..length] : string.Empty;
    }

    private static string TrailingContext(DocumentComparisonHunk hunk)
    {
        var offset = hunk.AfterStart + hunk.AfterLength - hunk.AfterContextStart;
        return offset >= 0 && offset < hunk.AfterContext.Length ? hunk.AfterContext[offset..] : string.Empty;
    }

    private static string Head(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    private static string Tail(string text, int length) => text.Length <= length ? text : "…" + text[^length..];

    private static void Add(List<RedlineSegment> segments, RedlineKind kind, string text)
    {
        if (text.Length > 0) segments.Add(new RedlineSegment(kind, text));
    }
}
