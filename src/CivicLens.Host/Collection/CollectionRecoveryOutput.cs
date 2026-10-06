namespace CivicLens.Host.Collection;

internal sealed record CollectionRecoveryOutput(string HandoffId, string Status,
    CollectionImportOutput? Import, string? Error);
