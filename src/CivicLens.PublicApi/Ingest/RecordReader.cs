using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>Bounds the texts that search indexes for a record and refuses a text that Postgres cannot store.</summary>
public static class RecordReader
{
    /// <summary>Refuses a NUL character and gives an action its bill line, which is also its snippet text. The entry view gives the bill line.</summary>
    public static IndexedRecord Finish(IndexedRecord indexed, string? billLine)
    {
        RequireNoNul(indexed);
        return indexed with { Entry = indexed.Entry with { BillLine = billLine }, SnippetText = billLine ?? indexed.SnippetText };
    }

    // Postgres cannot store a NUL character in text or jsonb.
    private static void RequireNoNul(IndexedRecord record)
    {
        var entry = record.Entry;
        IEnumerable<string?> texts =
            [entry.Headline, entry.SourceHost, record.Summary, record.Source, record.SnippetText, .. entry.Officials.SelectMany(official => new[] { official.Id, official.Name }),
                .. entry.IssueIds, .. entry.PolicyAreaIds];
        if (texts.Any(text => text?.Contains('\0', StringComparison.Ordinal) == true))
            throw new InvalidDataException("A record text holds a NUL character.");
    }

    /// <summary>Applies the one text bound rule to the summary and source parts of a record, in order.</summary>
    public static IndexedRecord Create(PublishedRecordEntry entry, string[] summary, string[] source, string? snippetText = null) =>
        new(entry, Bound(IndexedRecord.SummaryLimit, summary), Bound(IndexedRecord.SourceLimit, source), BoundSnippetText(snippetText));

    private static string? BoundSnippetText(string? text)
    {
        var bounded = Bound(IndexedRecord.SummaryLimit, text is null ? [] : [text]);
        return bounded.Length == 0 ? null : bounded;
    }

    private static string Bound(int limit, IEnumerable<string> parts)
    {
        var text = new StringBuilder();
        foreach (var part in parts.Where(part => part.Length > 0))
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(part.AsSpan(0, Math.Min(part.Length, limit)));
            if (text.Length >= limit) break;
        }
        if (text.Length > limit) text.Length = limit;
        if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text.Length--;
        return text.ToString();
    }
}
