using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Tests.Fixtures;

/// <summary>Stores an action revision with the row builder that production code uses, so that the columns and child rows are as a real write leaves them.
/// A revision by code or analysis gets a run ID that the revision key derives, because the table requires a unique one. It relaxes the foreign keys, because the test needs neither the draft nor the candidate. A second call for the same revision replaces the first.</summary>
internal static class ActionRevisionSeeder
{
    public static async Task AddAsync(string connectionString, ActionDraftRevision revision, string? runId = null)
    {
        runId ??= ReviewAuthor.KindOf(revision.AuthorSubject) == ReviewAuthorKind.Person ? null : Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"{revision.RecordId}:{revision.RevisionNumber}")));
        var row = typeof(PostgresActionReviewStore).Assembly.GetType($"{typeof(PostgresActionReviewStore).Namespace}.ActionRevisionRows")!
            .GetMethod("Create")!.Invoke(null, [revision, runId, TestCodeVersion.Current])!;
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM action_revision_sponsorship_citations WHERE record_id = {0} AND revision_number = {1};
            DELETE FROM action_revision_sponsorships WHERE record_id = {0} AND revision_number = {1};
            DELETE FROM action_revision_issues WHERE record_id = {0} AND revision_number = {1};
            DELETE FROM action_revisions WHERE record_id = {0} AND revision_number = {1}
            """, revision.RecordId, revision.RevisionNumber);
        db.Add(row);
        await db.SaveChangesAsync();
    }
}
