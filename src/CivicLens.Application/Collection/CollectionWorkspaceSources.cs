using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Application.Collection.Processing;

namespace CivicLens.Application.Collection;

/// <summary>Bounded source workspace data loaded for one owner-authorized page request.</summary>
public sealed record CollectionWorkspaceSources(
    IReadOnlyList<CollectionJobRecord> Jobs,
    CollectionJobRecord? SelectedJob,
    IReadOnlyDictionary<string, StoredCollectionAttempt> RetainedAttempts,
    DocumentHistory? History,
    bool HistoryLimitExceeded,
    bool HistoryUnavailable,
    IReadOnlyList<EvidenceProcessingRecord>? Processing = null,
    string? NewerCursor = null,
    string? OlderCursor = null,
    SourceHealthReport? Health = null);
