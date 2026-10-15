using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed record PendingRecord(PublishedRecordEntry Entry, int ReviewStateVersion, byte[] Json, string Page)
{
    public static PendingRecord Create<TRecord>(PublishedRecordEntry entry, int reviewStateVersion, TRecord record, string page)
        where TRecord : class =>
        new(entry, reviewStateVersion, JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions), page);

    public string RecordId => Entry.RecordId;

    public ReviewStateExpectation Expectation => new(Entry.Kind, Entry.RecordId, Entry.RevisionNumber, ReviewStateVersion);
}
