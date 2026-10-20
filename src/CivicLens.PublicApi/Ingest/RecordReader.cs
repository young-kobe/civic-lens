using System.Text;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>Reads one record file under the contract rules and picks the text that search indexes for its kind.</summary>
public static class RecordReader
{
    public static IndexedRecord Read(PublishedRecordEntry listed, ReadOnlySpan<byte> json)
    {
        if (json.IndexOf("\\u0000"u8) >= 0) throw new InvalidDataException("A record text holds a NUL character.");
        var indexed = listed.Kind switch
        {
            PublishedRecordKind.DocumentChange => Index(Parse<PublishedDocumentChange>(json)),
            PublishedRecordKind.Statement => Index(Parse<PublishedStatement>(json)),
            PublishedRecordKind.Action => Index(Parse<PublishedAction>(json)),
            PublishedRecordKind.Vote => Index(Parse<PublishedVote>(json)),
            PublishedRecordKind.Brief => Index(Parse<PublishedBrief>(json)),
            PublishedRecordKind.Relationship => Index(Parse<PublishedRelationship>(json)),
            _ => throw new InvalidDataException("The record kind is unknown.")
        };
        if (indexed.Entry.RecordId != listed.RecordId || indexed.Entry.RevisionNumber != listed.RevisionNumber)
            throw new InvalidDataException("The record file does not match its manifest entry.");
        return indexed;
    }

    private static T Parse<T>(ReadOnlySpan<byte> json) where T : class =>
        JsonSerializer.Deserialize<T>(json, PublicationProtocol.JsonOptions) ?? throw new InvalidDataException("A record file is empty.");

    private static IndexedRecord Index(PublishedDocumentChange record)
    {
        record.Validate();
        return Create(record.ToEntry(), [record.Summary], [record.After.Text, record.Before.Text]);
    }

    private static IndexedRecord Index(PublishedStatement record)
    {
        record.Validate();
        return Create(record.ToEntry(), [record.Summary, .. record.Quotations.Select(quotation => quotation.Quote)], [record.Source.Text]);
    }

    private static IndexedRecord Index(PublishedAction record)
    {
        record.Validate();
        return Create(record.ToEntry(), [record.Summary, record.BillTitle], [.. record.Sources.Select(source => source.Text)]);
    }

    private static IndexedRecord Index(PublishedVote record)
    {
        record.Validate();
        return Create(record.ToEntry(), [record.Summary], [.. record.Sources.Select(source => source.Text)]);
    }

    private static IndexedRecord Index(PublishedBrief record)
    {
        record.Validate();
        return Create(record.ToEntry(), [.. record.Paragraphs.Select(paragraph => paragraph.Text)], []);
    }

    private static IndexedRecord Index(PublishedRelationship record)
    {
        record.Validate();
        return Create(record.ToEntry(), [record.Reason, record.Quote], []);
    }

    private static IndexedRecord Create(PublishedRecordEntry entry, string[] summary, string[] source) =>
        new(entry, Bound(IndexedRecord.SummaryLimit, summary), Bound(IndexedRecord.SourceLimit, source));

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
