using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal static class AfterDecisionHooks
{
    public static Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> ForRecordKind(
        IDbContextFactory<CollectionAttemptDbContext> contextFactory, RelationshipRecordKind relationshipKind, PublishedRecordKind publishedKind) =>
        InOrder(
            RelationshipStaleness.AfterDecision(new(contextFactory, new RelationshipDraftQueueMapping()), relationshipKind),
            BriefStaleness.AfterDecision(new(contextFactory, new BriefDraftQueueMapping()), publishedKind));

    public static Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> InOrder(
        params Func<CollectionAttemptDbContext, string, int, CancellationToken, Task>[] hooks) =>
        async (db, recordId, revisionNumber, cancellationToken) =>
        {
            foreach (var hook in hooks) await hook(db, recordId, revisionNumber, cancellationToken);
        };
}
