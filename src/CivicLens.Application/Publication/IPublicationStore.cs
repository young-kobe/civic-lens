namespace CivicLens.Application.Publication;

public interface IPublicationStore
{
    Task<PublicationReleaseSummary?> GetLatestAsync(CancellationToken cancellationToken);
    Task<PublicationReleaseSummary?> GetAsync(int releaseNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
