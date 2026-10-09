using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection;

/// <summary>Bounded source workspace data loaded for one owner-authorized page request.</summary>
public sealed record CollectionWorkspaceSources(
    IReadOnlyList<CollectionJobRecord> Jobs,
    CollectionJobRecord? SelectedJob,
    IReadOnlyDictionary<string, StoredCollectionAttempt> RetainedAttempts,
    IReadOnlyDictionary<string, SourceHealth> ChecksByJobId,
    SourceHealthReport Health,
    string? NewerCursor = null,
    string? OlderCursor = null);
