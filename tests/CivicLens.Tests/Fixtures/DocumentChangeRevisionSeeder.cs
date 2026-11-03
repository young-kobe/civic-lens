using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Tests.Fixtures;

/// <summary>Stores a document change revision with the row builder that production code uses, so that the columns and child rows are as a real write leaves them.
/// It relaxes the foreign keys, because the test needs neither the draft nor the cited extractions.</summary>
internal static class DocumentChangeRevisionSeeder
{
    public static async Task AddAsync(string connectionString, DocumentChangeDraftRevision revision)
    {
        var row = typeof(PostgresVoteReviewStore).Assembly.GetType("CivicLens.Infrastructure.Review.DocumentChangeRevisionRows")!
            .GetMethod("Create")!.Invoke(null, [revision])!;
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        db.Add(row);
        await db.SaveChangesAsync();
    }
}
