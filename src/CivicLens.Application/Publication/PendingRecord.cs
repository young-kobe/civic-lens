using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal delegate Task<string> PageRenderer(ImmutableArray<PublishedRelationship> related, CancellationToken cancellationToken);

internal sealed record PendingRecord(PublishedRecordEntry Entry, int ReviewStateVersion, byte[] Json, PageRenderer RenderPage)
{
    public static PendingRecord Create<TRecord>(PublishedRecordEntry entry, int reviewStateVersion, TRecord record,
        PageRenderer renderPage)
        where TRecord : class =>
        new(entry, reviewStateVersion, JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions), renderPage);

    public string RecordId => Entry.RecordId;

    public ReviewStateExpectation Expectation => new(Entry.Kind, Entry.RecordId, Entry.RevisionNumber, ReviewStateVersion);
}
