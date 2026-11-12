using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Builds the redline of one changed passage from the exact offsets of the published change. Unchanged context shows before and after the change.</summary>
public static class DocumentChangeRedline
{
    public static IReadOnlyList<RedlineSegment> Build(PublishedChange change, string beforeText, string afterText)
    {
        var segments = new List<RedlineSegment>();
        Add(segments, RedlineKind.Context, LeadingContext(change, beforeText));
        AddChange(segments, change, beforeText, afterText);
        Add(segments, RedlineKind.Context, TrailingContext(change, afterText));
        return segments;
    }

    private static void AddChange(List<RedlineSegment> segments, PublishedChange change, string beforeText, string afterText)
    {
        if (change.WordEdits.Length == 0)
        {
            Add(segments, RedlineKind.Removed, beforeText.Substring(change.BeforeStart, change.BeforeLength));
            Add(segments, RedlineKind.Added, afterText.Substring(change.AfterStart, change.AfterLength));
            return;
        }
        var afterCursor = change.AfterStart;
        foreach (var edit in change.WordEdits)
        {
            Add(segments, RedlineKind.Same, afterText[afterCursor..edit.AfterStart]);
            Add(segments, RedlineKind.Removed, beforeText.Substring(edit.BeforeStart, edit.BeforeLength));
            Add(segments, RedlineKind.Added, afterText.Substring(edit.AfterStart, edit.AfterLength));
            afterCursor = edit.AfterStart + edit.AfterLength;
        }
        Add(segments, RedlineKind.Same, afterText[afterCursor..(change.AfterStart + change.AfterLength)]);
    }

    private static string LeadingContext(PublishedChange change, string beforeText) =>
        change.BeforeStart > change.BeforeContextStart ? beforeText[change.BeforeContextStart..change.BeforeStart] : string.Empty;

    private static string TrailingContext(PublishedChange change, string afterText)
    {
        var end = change.AfterStart + change.AfterLength;
        var contextEnd = change.AfterContextStart + change.AfterContextLength;
        return contextEnd > end ? afterText[end..contextEnd] : string.Empty;
    }

    private static void Add(List<RedlineSegment> segments, RedlineKind kind, string text)
    {
        if (text.Length > 0) segments.Add(new RedlineSegment(kind, text));
    }
}
