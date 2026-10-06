using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

public sealed record CollectionAttemptCompletion(CollectionImportDecision Decision, bool HandoffRemoved,
    string? CleanupFailure);

public enum CollectionRecoveryStatus
{
    ImportedAndRemoved,
    ImportedCleanupFailed,
    InvalidHandoff,
    ImportUnconfirmed,
    RecoveryFailed,
    PrerequisiteUnconfirmed,
    Cancelled
}
