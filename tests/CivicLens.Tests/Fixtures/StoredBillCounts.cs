using System.Reflection;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

/// <summary>Stores the bill count snapshot of a release through the production writer <c>ReleaseBillCounts.FillAsync</c>, as a publish commit does.</summary>
public static class StoredBillCounts
{
    /// <param name="onlyStoredRevisions">Skips a record state that has no stored revision, such as the placeholder vote of the ZIP seed. The writer refuses such a state.</param>
    public static async Task FillAsync(string database, int release, int congress, bool onlyStoredRevisions = false)
    {
        var bindings = await SelectedBindingsAsync(database, release, onlyStoredRevisions);
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await FillInTransactionAsync(db, release, congress, bindings);
        await transaction.CommitAsync();
    }

    public static async Task<PublishedRevisionBinding[]> SelectedBindingsAsync(string database, int release, bool onlyStoredRevisions = false)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT kind, record_id, revision_number FROM publication_record_states s
             WHERE kind IN ('action', 'vote') AND first_release <= @release AND (last_release IS NULL OR last_release >= @release)
               AND (NOT @stored OR (kind = 'action' AND EXISTS (SELECT 1 FROM action_revisions r WHERE r.record_id = s.record_id AND r.revision_number = s.revision_number))
                                OR (kind = 'vote' AND EXISTS (SELECT 1 FROM vote_revisions r WHERE r.record_id = s.record_id AND r.revision_number = s.revision_number)))
            """, connection);
        command.Parameters.AddWithValue("stored", onlyStoredRevisions);
        command.Parameters.AddWithValue("release", release);
        await using var reader = await command.ExecuteReaderAsync();
        var bindings = new List<PublishedRevisionBinding>();
        while (await reader.ReadAsync())
        {
            var kind = reader.GetString(0) == "action" ? PublishedRecordKind.Action : PublishedRecordKind.Vote;
            bindings.Add(new(kind, reader.GetString(1), reader.GetInt32(2)));
        }
        return [.. bindings];
    }

    public static async Task FillInTransactionAsync(CollectionAttemptDbContext db, int release, int congress, PublishedRevisionBinding[] bindings)
    {
        var type = typeof(PostgresPublicationStore).Assembly.GetType("CivicLens.Infrastructure.Publication.ReleaseBillCounts", throwOnError: true)!;
        var method = type.GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!;
        await (Task)method.Invoke(null, [db, release, (int?)congress, bindings, CancellationToken.None])!;
    }
}
