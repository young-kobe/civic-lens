using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

/// <summary>Atomically loads retained attempt and prior-capture state, decides, and retains a new attempt.</summary>
public interface ICollectionAttemptStore
{
    /// <summary>
    /// In one transaction, load any same-ID retained attempt and eligible prior capture, call
    /// <see cref="CollectionAttemptImport.Decide"/>, retain new evidence on NewAttempt, and return the decision.
    /// Enforce unique attempt identity and capture hash/length atomically. Retained state excludes per-call disposition.
    /// A conflicting replay must roll back without writes; honor cancellation and roll back on cancellation.
    /// </summary>
    Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
        CancellationToken cancellationToken);
}
