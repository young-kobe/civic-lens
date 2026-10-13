using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed record PendingRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber, int ReviewStateVersion,
    string Headline, DateTimeOffset FirstPublishedAtUtc, byte[] Json, string Page)
{
    public static PendingRecord Create<TRecord>(PublishedRecordKind kind, int reviewStateVersion, TRecord record,
        string recordId, int revisionNumber, string headline, DateTimeOffset firstPublishedAtUtc, string page)
        where TRecord : class =>
        new(kind, recordId, revisionNumber, reviewStateVersion, headline, firstPublishedAtUtc,
            JsonSerializer.SerializeToUtf8Bytes(record, PublicationProtocol.JsonOptions), page);

    public PublishedRecordEntry Entry => new()
    {
        Kind = Kind,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc
    };

    public ReviewStateExpectation Expectation => new(Kind, RecordId, RevisionNumber, ReviewStateVersion);
}
