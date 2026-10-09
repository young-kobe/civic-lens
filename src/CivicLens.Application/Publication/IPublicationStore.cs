namespace CivicLens.Application.Publication;

public interface IPublicationStore
{
    Task<PublicationState> GetStateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken);
    Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
