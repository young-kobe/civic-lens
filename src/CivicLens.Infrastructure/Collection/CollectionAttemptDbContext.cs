using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection;

/// <summary>Operational persistence for immutable collection evidence.</summary>
public sealed class CollectionAttemptDbContext(DbContextOptions<CollectionAttemptDbContext> options) : DbContext(options)
{
    internal DbSet<CaptureRow> Captures => Set<CaptureRow>();
    internal DbSet<AttemptRow> Attempts => Set<AttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollectionAttemptDbContext).Assembly);
    }
}
