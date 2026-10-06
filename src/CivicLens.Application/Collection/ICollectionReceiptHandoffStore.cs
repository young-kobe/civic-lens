namespace CivicLens.Application.Collection;

/// <summary>Durable pending receipts, separate from the evidence database and collector protocol.</summary>
/// <remarks>Callers hold a recovery lease across save/import/delete or load/verify/import/delete.
/// Implementations retain unsuccessful imports and never overwrite a saved attempt.</remarks>
public interface ICollectionReceiptHandoffStore
{
    /// <summary>Serializes fresh imports and replay within this spool until the lease is disposed.</summary>
    ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken);
    /// <summary>Returns only after a complete handoff has been saved; existing identities cannot be overwritten.</summary>
    Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken);
    /// <summary>Lists entry IDs without requiring valid envelope contents.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Loads a validated envelope whose attempt identity matches the entry ID.</summary>
    Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken);
    /// <summary>Removes a confirmed handoff only; capture artifacts are never removed.</summary>
    Task DeleteAsync(string handoffId, CancellationToken cancellationToken);
}
