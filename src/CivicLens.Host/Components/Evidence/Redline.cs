using CivicLens.Core.Documents;

namespace CivicLens.Host.Components.Evidence;

/// <summary>Turns a saved comparison hunk into reading-order segments: unchanged context, then struck and inserted words.</summary>
public static class Redline
{
    public static IReadOnlyList<RedlineSegment> Build(DocumentComparisonHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        var segments = new List<RedlineSegment>();
        Add(segments, RedlineKind.Context, LeadingContext(hunk));
        AddChange(segments, hunk);
        Add(segments, RedlineKind.Context, TrailingContext(hunk));
        return segments;
    }

    /// <summary>The changed words with a little unchanged text on each side, for one-line previews.</summary>
    public static IReadOnlyList<RedlineSegment> Excerpt(DocumentComparisonHunk hunk, int surroundingLength)
    {
        var change = new List<RedlineSegment>();
        AddChange(change, hunk);
        var first = change.FindIndex(segment => segment.Kind != RedlineKind.Same);
        var last = change.FindLastIndex(segment => segment.Kind != RedlineKind.Same);
        if (first < 0) return [];
        var excerpt = new List<RedlineSegment>();
        if (first > 0) Add(excerpt, RedlineKind.Same, Tail(change[first - 1].Text, surroundingLength));
        excerpt.AddRange(change.Skip(first).Take(last - first + 1));
        if (last + 1 < change.Count) Add(excerpt, RedlineKind.Same, Head(change[last + 1].Text, surroundingLength));
        return excerpt;
    }

    private static void AddChange(List<RedlineSegment> segments, DocumentComparisonHunk hunk)
    {
        if (hunk.WordEdits.IsDefaultOrEmpty)
        {
            Add(segments, RedlineKind.Removed, hunk.BeforeText);
            Add(segments, RedlineKind.Added, hunk.AfterText);
            return;
        }

        // Word edits use document offsets. Unchanged text between edits is identical in both versions.
        var afterCursor = 0;
        foreach (var edit in hunk.WordEdits)
        {
            var beforeIndex = edit.BeforeStart - hunk.BeforeStart;
            var afterIndex = edit.AfterStart - hunk.AfterStart;
            Add(segments, RedlineKind.Same, hunk.AfterText[afterCursor..afterIndex]);
            Add(segments, RedlineKind.Removed, hunk.BeforeText.Substring(beforeIndex, edit.BeforeLength));
            Add(segments, RedlineKind.Added, hunk.AfterText.Substring(afterIndex, edit.AfterLength));
            afterCursor = afterIndex + edit.AfterLength;
        }
        Add(segments, RedlineKind.Same, hunk.AfterText[afterCursor..]);
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
