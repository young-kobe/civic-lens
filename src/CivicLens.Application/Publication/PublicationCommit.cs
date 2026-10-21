using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublicationCommit(int ReleaseNumber, int? BaseReleaseNumber, string DirectoryName,
    ImmutableArray<PublishedRevisionBinding> Records,
    ImmutableArray<ReviewStateExpectation> AddedRecords)
{
    public ImmutableArray<PublishedRecordReference> WithdrawnRecords { get; init; } = [];
}
