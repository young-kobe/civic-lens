using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;

namespace CivicLens.Application.Collection;

/// <summary>Bounded source workspace data loaded for one owner-authorized page request.</summary>
public sealed record CollectionWorkspaceSources(
    IReadOnlyList<CollectionJobRecord> Jobs,
    CollectionJobRecord? SelectedJob,
    IReadOnlyDictionary<string, StoredCollectionAttempt> RetainedAttempts,
    DocumentHistory? History,
    bool HistoryLimitExceeded,
    bool HistoryUnavailable);
