using CivicLens.Application.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

public sealed class PostgresRecordKindStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRecordKindStore
{
    public async Task<PublishedRecordKind?> FindKindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewQueries.ReadRecordKindAsync(db, recordId, cancellationToken);
    }
}
