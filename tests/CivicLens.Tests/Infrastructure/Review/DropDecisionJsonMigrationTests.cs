using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DropDecisionJsonMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261101195619_DropDecisionJson";
    private static readonly string Concern = new('c', 32);
    private static readonly string Approval = new('a', 32);
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 10, 19, 1, 2, 3, TimeSpan.Zero).AddTicks(1_234_567);
    private static readonly (string Prefix, string IdColumn)[] Tables =
    [
        ("document_change", "draft_id"), ("statement", "candidate_id"), ("action", "record_id"), ("vote", "record_id"),
        ("executive_action", "record_id"), ("relationship", "record_id"), ("brief", "record_id")
    ];

    private string connectionString = null!;

    public static IEnumerable<object[]> TableData() => Tables.Select(table => new object[] { table.Prefix, table.IdColumn });

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [MemberData(nameof(TableData))]
    public async Task TheColumnsOfAMatchingDecisionSurviveAndTheJsonColumnGoesBecauseTheColumnsAreTheOnlyCopy(string prefix, string idColumn)
    {
        await InsertConcernAndApprovalAsync(prefix, idColumn);

        await MigrateToAsync(Migration);

        Assert.Equal([$"{Approval}|Approve|auth0|reviewer|Fixed.|Date corrected.|{Concern}", $"{Concern}|RequestChanges|auth0|reviewer|Context missing.||"],
            await ReadAsync($"""
                SELECT d.decision_id || '|' || d.kind || '|' || d.actor_subject || '|' || coalesce(d.note, '') || '|' || coalesce(d.correction_note, '') || '|'
                    || coalesce((SELECT string_agg(r.resolved_decision_id, ',') FROM {prefix}_review_decision_resolutions r WHERE r.decision_id = d.decision_id), '')
                FROM {prefix}_review_decisions d ORDER BY d.review_state_version DESC
                """));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'decision_json'"));
    }

    // The last table in the migration order holds the bad row, so a drop that is not atomic would leave the earlier tables without the column.
    [Theory]
    [InlineData("actor_subject", "'auth0|other'")]
    [InlineData("note", "'Other note.'")]
    [InlineData("note", "NULL")]
    [InlineData("correction_note", "'Other correction.'")]
    [InlineData("kind", "'WithdrawApproval'")]
    [InlineData("revision_number", "2")]
    [InlineData("review_state_version", "9")]
    [InlineData("created_at_utc_ticks", "1")]
    [InlineData("created_at_utc_ticks", "created_at_utc_ticks + 100")]
    public async Task AColumnThatDiffersFromTheJsonFailsTheMigrationNamesTheDecisionAndLeavesEveryJsonColumnBecauseNothingMayBeLostWithoutACopy(
        string column, string value)
    {
        await InsertConcernAndApprovalAsync("brief", "record_id");
        await ExecuteAsync($"SET session_replication_role = replica; UPDATE brief_review_decisions SET {column} = {value} WHERE decision_id = '{Concern}'");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop decision_json", failure.MessageText);
        Assert.Contains("brief_review_decisions", failure.MessageText);
        Assert.Contains(Concern, failure.MessageText);
        Assert.Equal(Tables.Length, (await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'decision_json'")).Count);
        await using var db = NewContext();
        Assert.Contains(Migration, await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [MemberData(nameof(TableData))]
    public async Task AnIdColumnThatDiffersFromTheJsonFailsTheMigrationBecauseTheRecordIdIsADecisionField(string prefix, string idColumn)
    {
        await InsertConcernAndApprovalAsync(prefix, idColumn);
        await ExecuteAsync($"SET session_replication_role = replica; UPDATE {prefix}_review_decisions SET {idColumn} = 'r2' WHERE decision_id = '{Concern}'");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop decision_json", failure.MessageText);
        Assert.Contains($"{prefix}_review_decisions", failure.MessageText);
        Assert.Contains(Concern, failure.MessageText);
    }

    [Fact]
    public async Task ADecisionIdThatDiffersFromTheJsonFailsTheMigrationAndNamesTheColumnValueBecauseTheIdIsADecisionField()
    {
        var other = new string('f', 32);
        await InsertConcernAndApprovalAsync("brief", "record_id");
        await ExecuteAsync($"SET session_replication_role = replica; UPDATE brief_review_decisions SET decision_id = '{other}' WHERE decision_id = '{Concern}'");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop decision_json", failure.MessageText);
        Assert.Contains(other, failure.MessageText);
        Assert.Equal(Tables.Length, (await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'decision_json'")).Count);
    }

    // These shapes are valid data, so a check that is too strict would stop the production migration.
    [Fact]
    public async Task ACodeApprovalWithoutANoteAnOlderRowWithoutACorrectionNoteKeyAndANoteWithNonAsciiTextAndAnApostropheMigrate()
    {
        var older = new string('d', 32);
        var text = new string('e', 32);
        await InsertAsync("vote", "record_id", Decision(Approval, 1, ReviewDecisionKind.Approve, null, [], null) with { ActorSubject = "civic-lens:code" });
        await InsertAsync("vote", "record_id", Decision(older, 2, ReviewDecisionKind.RequestChanges, "Older row.", [], null), json => json.Remove("correctionNote"));
        await InsertAsync("vote", "record_id", Decision(text, 3, ReviewDecisionKind.RequestChanges, "Caf\u00e9 d'accord \u00f1 \u65e5\u672c.", [], "L'\u00e9t\u00e9."));

        await MigrateToAsync(Migration);

        Assert.Equal(["<null>|civic-lens:code|<null>", "Older row.|auth0|reviewer|<null>", "Caf\u00e9 d'accord \u00f1 \u65e5\u672c.|auth0|reviewer|L'\u00e9t\u00e9."],
            await ReadAsync("""
                SELECT coalesce(note, '<null>') || '|' || actor_subject || '|' || coalesce(correction_note, '<null>')
                FROM vote_review_decisions ORDER BY review_state_version
                """));
    }

    [Theory]
    [InlineData("DELETE FROM brief_review_decision_resolutions")]
    [InlineData("UPDATE brief_review_decision_resolutions SET resolved_decision_id = decision_id")]
    public async Task AResolutionRowThatDiffersFromTheJsonFailsTheMigrationBecauseTheResolvedConcernsAreADecisionField(string change)
    {
        await InsertConcernAndApprovalAsync("brief", "record_id");
        await ExecuteAsync("SET session_replication_role = replica; " + change);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop decision_json", failure.MessageText);
        Assert.Contains(Approval, failure.MessageText);
        Assert.Equal(Tables.Length, (await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'decision_json'")).Count);
    }

    // A rollback goes one step down and, after the fix, up again. Both steps must keep every column and the old reader must read the JSON.
    [Theory]
    [MemberData(nameof(TableData))]
    public async Task TheRollbackRebuildsAJsonCopyThatReadsBackAsTheSameDecisionAndTheNextRunKeepsEveryColumnBecauseARollbackMustLoseNothing(
        string prefix, string idColumn)
    {
        var codeApproval = Decision(new string('d', 32), 3, ReviewDecisionKind.Approve, null, [], null) with { ActorSubject = "civic-lens:code" };
        var nonAscii = Decision(new string('e', 32), 4, ReviewDecisionKind.WithdrawApproval, "Caf\u00e9 d'accord \u00f1 \u65e5\u672c.", [], "L'\u00e9t\u00e9.");
        ReviewDecision[] decisions =
        [
            Decision(Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.", [], null),
            Decision(Approval, 2, ReviewDecisionKind.Approve, "Fixed.", [Concern], "Date corrected."),
            codeApproval,
            nonAscii
        ];
        foreach (var decision in decisions) await InsertAsync(prefix, idColumn, decision);
        var columnsBefore = await ReadColumnsAsync(prefix, idColumn);

        await MigrateToAsync(Migration);
        await MigrateToAsync(Before());

        var restored = (await ReadAsync($"SELECT decision_json FROM {prefix}_review_decisions ORDER BY review_state_version"))
            .Select(json => JsonSerializer.Deserialize<ReviewDecision>(json, JsonSerializerOptions.Web)!).ToList();
        Assert.Equal(decisions.Length, restored.Count);
        for (var index = 0; index < decisions.Length; index++) Assert.Equal(decisions[index], restored[index], DecisionComparer.Instance);

        await MigrateToAsync(Migration);

        Assert.Equal(columnsBefore, await ReadColumnsAsync(prefix, idColumn));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'decision_json'"));
    }

    private Task<List<string>> ReadColumnsAsync(string prefix, string idColumn) => ReadAsync($"""
        SELECT d.decision_id || '|' || d.{idColumn} || '|' || d.revision_number || '|' || d.review_state_version || '|' || d.kind || '|' || d.actor_subject
            || '|' || coalesce(d.note, '<null>') || '|' || coalesce(d.correction_note, '<null>') || '|' || d.created_at_utc_ticks || '|'
            || coalesce((SELECT string_agg(r.resolved_decision_id, ',') FROM {prefix}_review_decision_resolutions r WHERE r.decision_id = d.decision_id), '')
        FROM {prefix}_review_decisions d ORDER BY d.review_state_version
        """);

    private async Task InsertConcernAndApprovalAsync(string prefix, string idColumn)
    {
        await InsertAsync(prefix, idColumn, Decision(Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.", [], null));
        await InsertAsync(prefix, idColumn, Decision(Approval, 2, ReviewDecisionKind.Approve, "Fixed.", [Concern], "Date corrected."));
    }

    private static ReviewDecision Decision(string id, int version, ReviewDecisionKind kind, string? note, string[] resolved, string? correctionNote) =>
        new(id, "r1", 1, version, kind, "auth0|reviewer", note, [.. resolved], CreatedAt, correctionNote);

    private Task InsertAsync(string prefix, string idColumn, ReviewDecision decision, Action<JsonObject>? editJson = null)
    {
        var node = JsonSerializer.SerializeToNode(decision, JsonSerializerOptions.Web)!.AsObject();
        editJson?.Invoke(node);
        var json = Quote(node.ToJsonString());
        var resolutions = string.Concat(decision.ResolvedDecisionIds.Select(resolved => $"""
            INSERT INTO {prefix}_review_decision_resolutions (decision_id, {idColumn}, resolved_decision_id) VALUES ('{decision.DecisionId}', 'r1', '{resolved}');
            """));
        return ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO {prefix}_review_decisions (decision_id, {idColumn}, revision_number, review_state_version, decision_json, kind,
                actor_subject, note, correction_note, created_at_utc_ticks)
            VALUES ('{decision.DecisionId}', 'r1', 1, {decision.ReviewStateVersion}, {json}, '{decision.Kind}', '{decision.ActorSubject}',
                {Quote(decision.Note)}, {Quote(decision.CorrectionNote)}, {decision.CreatedAtUtc.UtcTicks});
            {resolutions}
            """);
    }

    private static string Quote(string? value) => value is null ? "NULL" : $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }

    private sealed class DecisionComparer : IEqualityComparer<ReviewDecision>
    {
        public static readonly DecisionComparer Instance = new();

        public bool Equals(ReviewDecision? x, ReviewDecision? y) =>
            x is not null && y is not null && (x with { ResolvedDecisionIds = [] }) == (y with { ResolvedDecisionIds = [] }) &&
            x.ResolvedDecisionIds.SequenceEqual(y.ResolvedDecisionIds);

        public int GetHashCode(ReviewDecision obj) => obj.DecisionId.GetHashCode(StringComparison.Ordinal);
    }
}
