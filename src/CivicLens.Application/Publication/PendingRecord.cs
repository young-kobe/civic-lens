using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed record PendingRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber, int ReviewStateVersion,
    string Headline, DateTimeOffset FirstPublishedAtUtc, byte[] Json, string Page)
{
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
