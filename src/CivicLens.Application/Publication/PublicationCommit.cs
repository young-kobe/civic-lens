using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed record PublicationCommit(int ReleaseNumber, int? BaseReleaseNumber, string DirectoryName,
    DateTimeOffset PublishedAtUtc, ImmutableArray<PublishedRevisionBinding> Records,
    ImmutableArray<ReviewStateExpectation> AddedRecords);
