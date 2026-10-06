using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

public sealed record CollectionRecoveryItem(string HandoffId, CollectionRecoveryStatus Status,
    CollectionImportDecision? Decision, string? Error);
