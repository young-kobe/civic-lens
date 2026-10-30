using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public interface IPublicationStore
{
    Task<PublicationState> GetStateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken);
    Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken);

    Task PruneAsync(int keep, TimeSpan minimumAge, Func<int, string?, CancellationToken, Task> retireOlder, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, ReviewStatus>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
