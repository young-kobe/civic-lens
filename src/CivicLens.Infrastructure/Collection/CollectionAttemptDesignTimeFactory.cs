using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CivicLens.Infrastructure.Collection;

internal sealed class CollectionAttemptDesignTimeFactory : IDesignTimeDbContextFactory<CollectionAttemptDbContext>
{
    public CollectionAttemptDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE") ??
                "Host=localhost;Database=civic_lens_design")
            .Options;
        return new CollectionAttemptDbContext(options);
    }
}
