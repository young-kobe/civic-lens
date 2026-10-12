using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StoreReviewStatusMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261012120000_MoveEnqueueTriggersToCSharp";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string schema = "review_status_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
    }

    public Task DisposeAsync() => ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    [Fact]
    public async Task BackfilledReviewStatusEqualsThePolicyStatusForEveryHistoryShape()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var histories = new Dictionary<string, (int CurrentRevision, ReviewDecision[] Decisions)>
        {
            ["1"] = (1, []),
            ["2"] = (1, [Decision("2", 1, 1, ReviewDecisionKind.RequestChanges, "2a")]),
            ["3"] = (2, [Decision("3", 1, 1, ReviewDecisionKind.RequestChanges, "3a")]),
            ["4"] = (1, [Decision("4", 1, 1, ReviewDecisionKind.Approve, "4a")]),
            ["5"] = (1, [Decision("5", 1, 1, ReviewDecisionKind.RequestChanges, "5a"),
                Decision("5", 1, 2, ReviewDecisionKind.Approve, "5b", "5a")]),
            ["6"] = (1, [Decision("6", 1, 1, ReviewDecisionKind.Approve, "6a"),
                Decision("6", 1, 2, ReviewDecisionKind.WithdrawApproval, "6b")]),
            ["7"] = (2, [Decision("7", 1, 1, ReviewDecisionKind.RequestChanges, "7a"),
                Decision("7", 2, 2, ReviewDecisionKind.Approve, "7b", "7a"),
                Decision("7", 2, 3, ReviewDecisionKind.WithdrawApproval, "7c"),
                Decision("7", 2, 4, ReviewDecisionKind.Approve, "7d", "7c")]),
            ["8"] = (3, [Decision("8", 1, 1, ReviewDecisionKind.Approve, "8a")])
        };
        await ExecuteAsync(connectionString, "SET session_replication_role = replica;" +
            string.Concat(histories.Select(history => SeedSql(history.Key, history.Value.CurrentRevision, history.Value.Decisions))));

        await db.Database.MigrateAsync();

        var stored = await ReadStatusesAsync();
        foreach (var (key, history) in histories)
        {
            var expected = ReviewPolicy.GetCurrentStatus(Revision(key, history.CurrentRevision).RevisionNumber,
                [.. history.Decisions]);
            Assert.Equal(expected.ToString(), stored[Id(key)]);
        }
        Assert.Equal(Enum.GetValues<ReviewStatus>().Count(status => status != ReviewStatus.NotDrafted), stored.Values.Distinct().Count());
    }

    [Fact]
    public async Task BackfilledHasChangesMatchesTheJsonRuleTheEligibleIndexUsed()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(connectionString, $$"""
            SET session_replication_role = replica;
            INSERT INTO document_comparisons (comparison_id, before_extraction_id, after_extraction_id, algorithm_version, settings_version, result_json)
            VALUES ('{{new string('1', 64)}}', 'b', 'a', 'v', 's', '{"status":0,"hunks":[{}]}'),
                   ('{{new string('2', 64)}}', 'b', 'a', 'v', 's', '{"status":0,"hunks":[]}'),
                   ('{{new string('3', 64)}}', 'b', 'a', 'v', 's', '{"status":1,"hunks":[]}');
            """);

        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT has_changes FROM document_comparisons ORDER BY comparison_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var flags = new List<bool>();
        while (await reader.ReadAsync()) flags.Add(reader.GetBoolean(0));
        Assert.Equal([true, false, false], flags);
    }

    private async Task<Dictionary<string, string>> ReadStatusesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT draft_id, review_status FROM document_change_drafts", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var statuses = new Dictionary<string, string>();
        while (await reader.ReadAsync()) statuses.Add(reader.GetString(0), reader.GetString(1));
        return statuses;
    }

    private static string Id(string key) => new(key[0], 32);

    private static DocumentChangeDraftRevision Revision(string key, int number) => new(Id(key), number, new string('c', 64),
        "auth0|owner", DateTimeOffset.UnixEpoch, "Headline", "Summary", null, null, "Institution", null, null, [], [], []);

    private static ReviewDecision Decision(string key, int revision, int version, ReviewDecisionKind kind, string tag,
        params string[] resolved) => new(DecisionId(tag), Id(key), revision, version, kind, "auth0|owner", "Note",
            [.. resolved.Select(DecisionId)], DateTimeOffset.UnixEpoch);

    private static string DecisionId(string tag) => (tag + new string('0', 32))[..32];

    private static string SeedSql(string key, int currentRevision, ReviewDecision[] decisions)
    {
        var revisions = string.Concat(Enumerable.Range(1, currentRevision).Select(number =>
            $"INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks) VALUES ('{Id(key)}', {number}, '{{}}', 0);"));
        var rows = string.Concat(decisions.Select(decision =>
            $"INSERT INTO document_change_review_decisions (decision_id, draft_id, revision_number, review_state_version, decision_json, kind, created_at_utc_ticks) VALUES ('{decision.DecisionId}', '{decision.DraftId}', {decision.RevisionNumber}, {decision.ReviewStateVersion}, '{JsonSerializer.Serialize(decision, JsonOptions)}', '{decision.Kind}', 0);"));
        return $"INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, created_at_utc_ticks) VALUES ('{Id(key)}', '{new string('c', 64)}', {currentRevision}, {decisions.Length}, 0);" + revisions + rows;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
