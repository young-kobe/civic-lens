using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Review;

/// <summary>What a record kind adds to every approval that a person gives. <paramref name="Check"/> runs before the decision and may refuse it.
/// <paramref name="Apply"/> runs after the decision, in the same transaction. A code approval runs neither.</summary>
internal sealed record PersonApprovalRules<TDraftRow>(Func<CollectionAttemptDbContext, TDraftRow, CancellationToken, Task>? Check = null,
    Func<CollectionAttemptDbContext, TDraftRow, ReviewDecision, CancellationToken, Task>? Apply = null);
