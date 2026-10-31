using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed partial class ReviewDecisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    public const string Migration = "20261031041526_ReviewDecisionColumns";
    private static readonly string Concern = new('c', 32);
    private static readonly string Approval = new('a', 32);
    private static readonly string[] HiddenColumns = ["actor_subject", "note", "correction_note", "decision_json"];

    private string connectionString = null!;
    private bool migrated;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // The JSON holds the kind as a number and the column holds its name, so the copy must map one to the other.
    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task TheBackfillCopiesTheActorTheNotesAndTheResolvedConcernsOfEachKindFromTheJson(string prefix, string idColumn)
    {
        await InsertDecisionAsync(prefix, idColumn, "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.");
        await InsertDecisionAsync(prefix, idColumn, "r1", Approval, 2, ReviewDecisionKind.Approve, "Fixed.", [Concern], "Date corrected.");

        await MigrateForwardAsync();

        Assert.Equal([$"{Approval}|Approve|auth0|reviewer|Fixed.|Date corrected.", $"{Concern}|RequestChanges|auth0|reviewer|Context missing.|"],
            await ReadAsync($"""
                SELECT decision_id || '|' || kind || '|' || actor_subject || '|' || coalesce(note, '') || '|' || coalesce(correction_note, '')
                FROM {prefix}_review_decisions ORDER BY review_state_version DESC
                """));
        Assert.Equal([$"{Approval}|{Concern}|r1"],
            await ReadAsync($"SELECT decision_id || '|' || resolved_decision_id || '|' || {idColumn} FROM {prefix}_review_decision_resolutions"));
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task AKindColumnThatDiffersFromTheJsonFailsTheMigrationAndNamesTheTableAndTheDecisionBecauseTheColumnsMustEqualTheJson(
        string prefix, string idColumn)
    {
        await InsertDecisionAsync(prefix, idColumn, "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.", kindColumn: "Approve");

        var failure = await Assert.ThrowsAsync<PostgresException>(MigrateForwardAsync);

        Assert.Contains($"{prefix}_review_decisions", failure.MessageText);
        Assert.Contains(Concern, failure.MessageText);
    }

    [Fact]
    public async Task ADecisionJsonWithoutAnActorFailsTheMigrationAndLeavesTheSchemaUnchangedBecauseEveryDecisionNeedsAnActor()
    {
        await InsertDecisionAsync("vote", "record_id", "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "x", editJson: json => json.Remove("actorSubject"));

        await Assert.ThrowsAsync<PostgresException>(MigrateForwardAsync);

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'actor_subject' AND table_name = 'vote_review_decisions'"));
        await using var db = NewContext();
        Assert.Contains(Migration, await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("decisionId", "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"")]
    [InlineData("draftId", "\"r2\"")]
    [InlineData("revisionNumber", "9")]
    [InlineData("reviewStateVersion", "9")]
    [InlineData("createdAtUtc", "\"2026-10-19T00:00:00.000002+00:00\"")]
    public async Task AJsonValueThatDiffersFromItsColumnFailsTheMigrationAndNamesTheTableAndTheDecisionBecauseTheColumnsMustEqualTheJson(
        string field, string jsonValue)
    {
        await InsertDecisionAsync("vote", "record_id", "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.",
            editJson: json => json[field] = JsonNode.Parse(jsonValue));

        var failure = await Assert.ThrowsAsync<PostgresException>(MigrateForwardAsync);

        Assert.Contains("vote_review_decisions", failure.MessageText);
        Assert.Contains(Concern, failure.MessageText);
    }

    // timestamptz rounds the seventh digit of a tick count, so a production write must pass the time check.
    [Fact]
    public async Task ACreationTimeWithATickBeyondTheMicrosecondPassesTheMigrationBecauseTimestamptzRoundsIt()
    {
        var createdAt = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        await InsertDecisionAsync("vote", "record_id", "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "x", createdAt: createdAt);

        await MigrateForwardAsync();

        Assert.Equal([Concern], await ReadAsync("SELECT decision_id FROM vote_review_decisions"));
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task AResolutionWithAWrongParentOrTargetKindFailsTheMigration(string prefix, string idColumn)
    {
        await InsertDecisionAsync(prefix, idColumn, "r1", Approval, 1, ReviewDecisionKind.Approve, null);
        await InsertDecisionAsync(prefix, idColumn, "r1", Concern, 2, ReviewDecisionKind.RequestChanges, "Parent is not an approval.", [Approval]);

        var parentFailure = await Assert.ThrowsAsync<PostgresException>(MigrateForwardAsync);

        Assert.Contains($"{prefix}_review_decisions", parentFailure.MessageText);
        Assert.Contains(Concern, parentFailure.MessageText);

        await ExecuteAsync($"DELETE FROM {prefix}_review_decisions WHERE decision_id = '{Concern}'");
        var approvalTwo = new string('d', 32);
        await InsertDecisionAsync(prefix, idColumn, "r1", approvalTwo, 3, ReviewDecisionKind.Approve, "Target is not a concern.", [Approval]);

        var targetFailure = await Assert.ThrowsAsync<PostgresException>(MigrateForwardAsync);

        Assert.Contains($"{prefix}_review_decisions", targetFailure.MessageText);
        Assert.Contains(approvalTwo, targetFailure.MessageText);
    }

    [Fact]
    public async Task OlderRowsWithoutACorrectionNoteKeyOrAResolvedKeyMigrateWithNoNoteAndNoResolutions()
    {
        await InsertDecisionAsync("vote", "record_id", "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "x",
            editJson: json => { json.Remove("correctionNote"); json.Remove("resolvedDecisionIds"); });

        await MigrateForwardAsync();

        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(correction_note, '<null>') FROM vote_review_decisions"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM vote_review_decision_resolutions"));
    }

    [Fact]
    public async Task AnEmptyNoteStaysEmptyAndAMissingNoteStaysNullBecauseTheyAreDifferentFacts()
    {
        await InsertDecisionAsync("vote", "record_id", "r1", Approval, 1, ReviewDecisionKind.Approve, "");
        await InsertDecisionAsync("vote", "record_id", "r1", Concern, 2, ReviewDecisionKind.RequestChanges, null);

        await MigrateForwardAsync();

        Assert.Equal(["", "<null>"], await ReadAsync("SELECT coalesce(note, '<null>') FROM vote_review_decisions ORDER BY review_state_version"));
    }

    [Fact]
    public async Task TheKindColumnRefusesAValueThatTheEnumLacks()
    {
        await MigrateForwardAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertDecisionAsync("vote", "record_id", "r1", Concern, 1,
            ReviewDecisionKind.RequestChanges, "x", kindColumn: "Bogus"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal("ck_vote_review_decisions_kind", failure.ConstraintName);
    }

    // An approval of record one must not resolve a concern of record two, even when both IDs exist.
    [Fact]
    public async Task AnApprovalCannotResolveAConcernOfAnotherRecordBecauseTheForeignKeyIncludesTheRecord()
    {
        await MigrateForwardAsync();
        await InsertDecisionAsync("action", "record_id", "r1", Approval, 1, ReviewDecisionKind.Approve, null);
        await InsertDecisionAsync("action", "record_id", "r1", new string('d', 32), 2, ReviewDecisionKind.RequestChanges, "Own concern.");
        await InsertDecisionAsync("action", "record_id", "r2", Concern, 1, ReviewDecisionKind.RequestChanges, "Other concern.");

        await ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{Approval}', 'r1', '{new string('d', 32)}')");
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{Approval}', 'r1', '{Concern}')"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task AResolutionCannotNameAnApprovalOfAnotherRecordBecauseTheForeignKeyIncludesTheRecord()
    {
        await MigrateForwardAsync();
        await InsertDecisionAsync("action", "record_id", "r1", Approval, 1, ReviewDecisionKind.Approve, null);
        await InsertDecisionAsync("action", "record_id", "r2", Concern, 1, ReviewDecisionKind.RequestChanges, "Other concern.");

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{Approval}', 'r2', '{Concern}')"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    // The read-only role hides a column by name, so each new column that holds a reviewer or a note must match the pattern.
    [Fact]
    public async Task EveryNewColumnThatHoldsAnActorOrANoteIsHiddenFromTheReadOnlyRoleAndTheResolutionColumnsAreNot()
    {
        await MigrateForwardAsync();
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql"));
        var literals = HiddenColumnAssignment().Match(script).Groups["value"].Value;
        var hidden = new Regex(string.Concat(QuotedLiteral().Matches(literals).Select(match => match.Groups[1].Value)));
        var columns = await ReadAsync("""
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name ~ '_review_decision(s|_resolutions)$'
            """);

        Assert.All(HiddenColumns, hiddenColumn => Assert.Equal(7, columns.Count(entry => entry.EndsWith("." + hiddenColumn, StringComparison.Ordinal))));
        Assert.All(columns, entry =>
        {
            var column = entry[(entry.IndexOf('.') + 1)..];
            Assert.Equal(HiddenColumns.Contains(column), hidden.IsMatch(column));
        });
    }

    [Fact]
    public async Task TheMigrationGoesDownAndUpAgainWithRowsPresentBecauseARollbackMustLeaveNoConstraintBehind()
    {
        await InsertDecisionAsync("action", "record_id", "r1", Concern, 1, ReviewDecisionKind.RequestChanges, "Context missing.");
        await InsertDecisionAsync("action", "record_id", "r1", Approval, 2, ReviewDecisionKind.Approve, "Fixed.", [Concern]);
        await MigrateForwardAsync();
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.tables WHERE table_name LIKE '%_review_decision_resolutions'"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE table_name LIKE '%_review_decisions' AND column_name IN ('actor_subject', 'note', 'correction_note')"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM pg_constraint WHERE conname LIKE 'ck_%_review_decisions_kind' OR conname LIKE 'AK_%_review_decisions_%'"));

        await MigrateForwardAsync();

        Assert.Equal([$"{Approval}|{Concern}"], await ReadAsync("SELECT decision_id || '|' || resolved_decision_id FROM action_review_decision_resolutions"));
    }

    private Task InsertDecisionAsync(string prefix, string idColumn, string recordId, string decisionId, int version, ReviewDecisionKind kind,
        string? note, string[]? resolved = null, string? correctionNote = null, string? kindColumn = null, Action<JsonObject>? editJson = null, DateTimeOffset? createdAt = null)
    {
        var decision = new ReviewDecision(decisionId, recordId, 1, version, kind, "auth0|reviewer", note, [.. resolved ?? []],
            createdAt ?? new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), correctionNote);
        var json = JsonSerializer.SerializeToNode(decision, JsonSerializerOptions.Web)!.AsObject();
        editJson?.Invoke(json);
        return ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO {prefix}_review_decisions (decision_id, {idColumn}, revision_number, review_state_version, decision_json, kind, created_at_utc_ticks
                {(migrated ? ", actor_subject" : "")})
            VALUES ('{decisionId}', '{recordId}', 1, {version}, '{json.ToJsonString()}',
                '{kindColumn ?? kind.ToString()}', {decision.CreatedAtUtc.UtcTicks}{(migrated ? ", 'auth0|reviewer'" : "")})
            """);
    }

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateForwardAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        migrated = true;
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

    [GeneratedRegex(@"hidden_column constant text :=(?<value>.*?);", RegexOptions.Singleline)]
    private static partial Regex HiddenColumnAssignment();

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex QuotedLiteral();
}
