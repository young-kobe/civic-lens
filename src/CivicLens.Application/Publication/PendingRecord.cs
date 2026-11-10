using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal delegate Task<string> PageRenderer(ImmutableArray<PublishedRelationship> related, ReleaseBillLines billLines, CancellationToken cancellationToken);

internal record PendingRecord(PublishedRecordEntry Entry, int ReviewStateVersion, byte[] Json, PublishedCorrection[] Corrections, PageRenderer RenderPage)
{
    public static PendingRecord<TRecord> Create<TRecord>(PublishedRecordEntry entry, int reviewStateVersion, TRecord record,
        PublishedCorrection[] corrections, PageRenderer renderPage)
        where TRecord : class =>
        new(entry, reviewStateVersion, record, JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions), corrections, renderPage);

    public string RecordId => Entry.RecordId;

    public ReviewStateExpectation Expectation => new(Entry.Kind, Entry.RecordId, Entry.RevisionNumber, ReviewStateVersion);
}

internal sealed record PendingRecord<TRecord>(PublishedRecordEntry Entry, int ReviewStateVersion, TRecord Record, byte[] Json,
    PublishedCorrection[] Corrections, PageRenderer RenderPage) : PendingRecord(Entry, ReviewStateVersion, Json, Corrections, RenderPage);
