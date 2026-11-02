using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Review;

/// <summary>What a record kind adds when a person approves a rejected revision. <paramref name="Check"/> runs before the decision and may refuse it.
/// <paramref name="Apply"/> runs after the decision, in the same transaction.</summary>
internal sealed record ReversalRules<TDraftRow>(Func<CollectionAttemptDbContext, TDraftRow, CancellationToken, Task>? Check = null,
    Func<CollectionAttemptDbContext, TDraftRow, DateTimeOffset, CancellationToken, Task>? Apply = null);
