using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Tests.Fixtures;

/// <summary>Stores a statement revision with the row builder that production code uses, so that the columns and child rows are as a real write leaves them.
/// It relaxes the foreign keys, because the test needs neither the draft nor the candidate. A second call for the same revision replaces the first.</summary>
internal static class StatementRevisionSeeder
{
    public static async Task AddAsync(string connectionString, StatementDraftRevision revision, string? runId = null)
    {
        var row = typeof(PostgresStatementReviewStore).Assembly.GetType($"{typeof(PostgresStatementReviewStore).Namespace}.StatementRevisionRows")!
            .GetMethod("Create")!.Invoke(null, [revision, runId])!;
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM statement_revision_speakers WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revision_other_speakers WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revision_issues WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revision_policy_areas WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revision_quotations WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revision_bill_numbers WHERE candidate_id = {0} AND revision_number = {1};
            DELETE FROM statement_revisions WHERE candidate_id = {0} AND revision_number = {1}
            """, revision.CandidateId, revision.RevisionNumber);
        db.Add(row);
        await db.SaveChangesAsync();
    }
}
