using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublicationCommit(int ReleaseNumber, int? BaseReleaseNumber, string DirectoryName,
    ImmutableArray<PublishedRevisionBinding> Records,
    ImmutableArray<ReviewStateExpectation> AddedRecords)
{
    public ImmutableArray<PublishedRecordReference> WithdrawnRecords { get; init; } = [];

    public required ImmutableArray<PublishedWithdrawal> Withdrawals { get; init; }


    public ImmutableArray<PublishedRecordEntry> AddedEntries { get; init; } = [];
}
