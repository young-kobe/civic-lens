using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Review;

internal static class AfterDecisionHooks
{
    public static Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> InOrder(
        params Func<CollectionAttemptDbContext, string, int, CancellationToken, Task>[] hooks) =>
        async (db, recordId, revisionNumber, cancellationToken) =>
        {
            foreach (var hook in hooks) await hook(db, recordId, revisionNumber, cancellationToken);
        };
}
