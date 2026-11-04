using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IPublicationStore
{
    Task<PublicationState> GetStateAsync(CancellationToken cancellationToken);
    Task<TimeSpan?> ReadAgeOfNewestReleaseAsync(CancellationToken cancellationToken);
    Task<int> CountApprovedOutsideActiveReleaseAsync(IReadOnlyCollection<PublishedRecordReference> withdrawn, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken);
    Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken);

    Task PruneAsync(Func<ReleaseRetentionPlan, string?, CancellationToken, Task> retire, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, ReviewedRecord>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRevisionBinding> published, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset>> ReadFirstPublishedAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken);

    Task<int> AddFirstPublishedAsync(IReadOnlyCollection<PublishedFirstPublication> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationReleaseSummary>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishedEntryCandidate>> ReadEntryCandidatesAsync(CancellationToken cancellationToken);

    Task<int> AddEntriesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
