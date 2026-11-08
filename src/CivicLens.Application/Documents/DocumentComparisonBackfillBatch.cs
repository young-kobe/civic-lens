using System.Collections.Immutable;

namespace CivicLens.Application.Documents;

public sealed record DocumentComparisonBackfillBatch(int Examined, ImmutableArray<string> Skipped,
    string? LastComparisonId);
