using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>Wraps a store so that a test can run code before the release list, after the candidate read, and before each batch insert.</summary>
internal sealed class InterceptingPublicationStore(IPublicationStore inner) : IPublicationStore
{
    private int batches;

    public Func<Task>? AfterReadingCandidates { get; set; }

    public Func<Task>? BeforeListingAll { get; set; }

    public Func<int, Task>? BeforeAddingEntries { get; set; }

    public async Task<IReadOnlyList<PublishedEntryCandidate>> ReadEntryCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = await inner.ReadEntryCandidatesAsync(cancellationToken);
        if (AfterReadingCandidates is not null) await AfterReadingCandidates();
        return candidates;
    }

    public async Task<int> AddEntriesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        if (BeforeAddingEntries is not null) await BeforeAddingEntries(++batches);
        return await inner.AddEntriesAsync(entries, cancellationToken);
    }

    public Task<int> FillBillLinesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        inner.FillBillLinesAsync(entries, cancellationToken);

    public Task<int> FillStatementApproversAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        inner.FillStatementApproversAsync(entries, cancellationToken);

    public Task<int> AddBillStatusesAsync(int releaseNumber, IReadOnlyCollection<PublishedBillStatus> statuses, CancellationToken cancellationToken) =>
        inner.AddBillStatusesAsync(releaseNumber, statuses, cancellationToken);

    public Task<StoredRelease> ReadReleaseAsync(int releaseNumber, CancellationToken cancellationToken) => inner.ReadReleaseAsync(releaseNumber, cancellationToken);

    public Task<PublicationState> GetStateAsync(CancellationToken cancellationToken) => inner.GetStateAsync(cancellationToken);

    public Task<TimeSpan?> ReadAgeOfNewestReleaseAsync(CancellationToken cancellationToken) => inner.ReadAgeOfNewestReleaseAsync(cancellationToken);

    public Task<int> CountApprovedOutsideActiveReleaseAsync(IReadOnlyCollection<PublishedRecordReference> withdrawn, CancellationToken cancellationToken) =>
        inner.CountApprovedOutsideActiveReleaseAsync(withdrawn, cancellationToken);

    public Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken) => inner.ListAsync(limit, cancellationToken);

    public async Task<IReadOnlyList<PublicationReleaseSummary>> ListAllAsync(CancellationToken cancellationToken)
    {
        if (BeforeListingAll is not null) await BeforeListingAll();
        return await inner.ListAllAsync(cancellationToken);
    }

    public Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken) => inner.ActivateAsync(releaseNumber, requireFiles, cancellationToken);

    public Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve, CancellationToken cancellationToken) =>
        inner.ServeActiveAsync(serve, cancellationToken);

    public Task PruneAsync(Func<ReleaseRetentionPlan, string?, CancellationToken, Task> retire, CancellationToken cancellationToken) =>
        inner.PruneAsync(retire, cancellationToken);

    public Task<IReadOnlyDictionary<PublishedRecordReference, ReviewedRecord>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRevisionBinding> published, CancellationToken cancellationToken) =>
        inner.ReadReviewStatusesAsync(published, cancellationToken);

    public Task<IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset>> ReadFirstPublishedAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken) =>
        inner.ReadFirstPublishedAsync(records, cancellationToken);

    public Task<int> AddFirstPublishedAsync(IReadOnlyCollection<PublishedFirstPublication> records, CancellationToken cancellationToken) =>
        inner.AddFirstPublishedAsync(records, cancellationToken);

    public Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => inner.FindReplayAsync(actorSubject, idempotencyKey, payloadHash, cancellationToken);

    public Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => inner.CommitAsync(actorSubject, commit, idempotencyKey, payloadHash, cancellationToken);
}
