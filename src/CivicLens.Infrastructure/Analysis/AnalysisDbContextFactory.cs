using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisDbContextFactory
{
    public static IDbContextFactory<CollectionAttemptDbContext> Create(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PooledDbContextFactory<CollectionAttemptDbContext>(options);
    }
}
