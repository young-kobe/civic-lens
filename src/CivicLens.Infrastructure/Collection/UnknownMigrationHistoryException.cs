namespace CivicLens.Infrastructure.Collection;

/// <summary>The migration history is neither empty, nor the baseline, nor the exact pre-squash chain. The message holds only migration IDs,
/// so the command can print it.</summary>
public sealed class UnknownMigrationHistoryException(string message) : InvalidOperationException(message);
