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
    public ImmutableArray<PublishedRecordEntry> RewrittenEntries { get; init; } = [];

    public ImmutableArray<VoteStoredColumns> VoteColumns { get; init; } = [];

    public required ReleaseNames Names { get; init; }

    /// <summary>The bill statuses that the release pins. They equal the rows of the status file of the release.</summary>
    public ImmutableArray<PublishedBillStatus> BillStatuses { get; init; } = [];
}
