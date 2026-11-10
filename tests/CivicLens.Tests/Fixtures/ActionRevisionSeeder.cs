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
        await AddManyAsync(connectionString, [revision], item => runId ?? DefaultRunId(item));
    }

    public static async Task AddManyAsync(string connectionString, IEnumerable<ActionDraftRevision> revisions)
        => await AddManyAsync(connectionString, revisions, DefaultRunId);

    private static async Task AddManyAsync(string connectionString, IEnumerable<ActionDraftRevision> revisions,
        Func<ActionDraftRevision, string?> runIdOf)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        var batch = new List<ActionDraftRevision>(128);
        foreach (var revision in revisions)
        {
            batch.Add(revision);
            if (batch.Count == batch.Capacity)
            {
                await WriteBatchAsync(db, batch, runIdOf);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await WriteBatchAsync(db, batch, runIdOf);
    }

    private static async Task WriteBatchAsync(CollectionAttemptDbContext db, IReadOnlyCollection<ActionDraftRevision> revisions,
        Func<ActionDraftRevision, string?> runIdOf)
    {
        var rows = revisions.Select(revision => CreateRow(revision, runIdOf(revision))).ToArray();
        var ids = revisions.Select(revision => revision.RecordId).ToArray();
        var numbers = revisions.Select(revision => revision.RevisionNumber).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM action_revision_sponsorship_citations child USING unnest({0}::text[], {1}::integer[]) AS named(id, number)
             WHERE child.record_id = named.id AND child.revision_number = named.number;
            DELETE FROM action_revision_sponsorships child USING unnest({0}::text[], {1}::integer[]) AS named(id, number)
             WHERE child.record_id = named.id AND child.revision_number = named.number;
            DELETE FROM action_revision_issues child USING unnest({0}::text[], {1}::integer[]) AS named(id, number)
             WHERE child.record_id = named.id AND child.revision_number = named.number;
            DELETE FROM action_revision_text_citations child USING unnest({0}::text[], {1}::integer[]) AS named(id, number)
             WHERE child.record_id = named.id AND child.revision_number = named.number;
            DELETE FROM action_revisions child USING unnest({0}::text[], {1}::integer[]) AS named(id, number)
             WHERE child.record_id = named.id AND child.revision_number = named.number;
            """, ids, numbers);
        db.AddRange(rows);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
    }

    private static string? DefaultRunId(ActionDraftRevision revision) =>
        ReviewAuthor.KindOf(revision.AuthorSubject) == ReviewAuthorKind.Person ? null :
            Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"{revision.RecordId}:{revision.RevisionNumber}")));

    private static object CreateRow(ActionDraftRevision revision, string? runId)
    {
        return typeof(PostgresActionReviewStore).Assembly.GetType($"{typeof(PostgresActionReviewStore).Namespace}.ActionRevisionRows")!
            .GetMethod("Create")!.Invoke(null, [revision, runId, TestCodeVersion.Current])!;
    }
}
