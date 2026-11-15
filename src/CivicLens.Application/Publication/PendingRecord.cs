using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal record PendingRecord(PublishedRecordEntry Entry, int ReviewStateVersion, byte[] Json, PublishedCorrection[] Corrections)
{
    public static PendingRecord<TRecord> Create<TRecord>(PublishedRecordEntry entry, int reviewStateVersion, TRecord record,
        PublishedCorrection[] corrections)
        where TRecord : class =>
        new(entry, reviewStateVersion, record, JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions), corrections);

    public string RecordId => Entry.RecordId;

    public ReviewStateExpectation Expectation => new(Entry.Kind, Entry.RecordId, Entry.RevisionNumber, ReviewStateVersion);
}

internal sealed record PendingRecord<TRecord>(PublishedRecordEntry Entry, int ReviewStateVersion, TRecord Record, byte[] Json,
    PublishedCorrection[] Corrections) : PendingRecord(Entry, ReviewStateVersion, Json, Corrections);
