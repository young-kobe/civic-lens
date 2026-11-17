using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RejectDecisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private int version;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    public static TheoryData<string, string> Tables
    {
        get
        {
            TheoryData<string, string> tables = [];
            foreach (var row in Kinds) tables.Add((string)row[0], (string)row[1]);
            return tables;
        }
    }

    public static TheoryData<string, string, PublishedRecordKind> Kinds => new()
    {
        { "document_change", "draft_id", PublishedRecordKind.DocumentChange },
        { "statement", "candidate_id", PublishedRecordKind.Statement },
        { "action", "record_id", PublishedRecordKind.Action },
        { "vote", "record_id", PublishedRecordKind.Vote },
        { "executive_action", "record_id", PublishedRecordKind.ExecutiveAction },
        { "relationship", "record_id", PublishedRecordKind.Relationship },
        { "brief", "record_id", PublishedRecordKind.Brief }
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task ARejectionWithoutAReasonCodeFailsTheDatabaseCheckOfEveryKindBecauseRejectionsAreCountedByCause(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, null));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code_kind", failure.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task AReasonCodeOnAnotherDecisionKindFailsTheDatabaseCheckOfEveryKind(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(prefix, idColumn, ReviewDecisionKind.RequestChanges, nameof(ReviewReasonCode.Duplicate)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code_kind", failure.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task AReasonCodeThatTheEnumLacksFailsTheDatabaseCheckOfEveryKind(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, "Bogus"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code", failure.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task EveryReasonCodeOfTheKindIsStoredForARejectionOfThatKind(string prefix, string idColumn, PublishedRecordKind kind)
    {
        foreach (var code in ReviewReasons.For(kind))
            await InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, code.ToString(), Guid.NewGuid().ToString("N"));

        Assert.Equal(ReviewReasons.For(kind).Length, await CountAsync(prefix));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AReasonCodeThatAnotherKindOwnsFailsTheDatabaseCheckOfThisKindBecauseEachKindHasItsOwnList(string prefix, string idColumn,
        PublishedRecordKind kind)
    {
        var foreign = Enum.GetValues<ReviewReasonCode>().First(code => !ReviewReasons.For(kind).Contains(code));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, foreign.ToString()));

        Assert.Equal($"ck_{prefix}_review_decisions_reason_code", failure.ConstraintName);
    }

    // The reason code names a cause and holds no person, so reports of rejections by cause may read it.
    [Fact]
    public async Task TheReasonCodeColumnIsNotHiddenFromTheReadOnlyRoleOfAnyDecisionTable()
    {
        var hidden = ReadOnlyGrants.HiddenColumn();

        var tables = await ReadAsync("SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'reason_code' AND table_name ~ '_review_decisions$'");

        Assert.Equal(7, tables.Count);
        Assert.All(new[] { "actor_subject", "note" }, column => Assert.Matches(hidden, column));
        Assert.DoesNotMatch(hidden, "reason_code");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task AKindThatTheEnumLacksFailsTheDatabaseCheckOfEveryKind(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, "Bogus", null));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_kind", failure.ConstraintName);
    }

    // An approval of record one must not resolve a concern of record two, even when both IDs exist.
    [Fact]
    public async Task AnApprovalCannotResolveAConcernOfAnotherRecordBecauseTheForeignKeyIncludesTheRecord()
    {
        var (approval, ownConcern, otherConcern) = (new string('a', 32), new string('d', 32), new string('c', 32));
        await InsertDecisionsAsync((approval, "r1", ReviewDecisionKind.Approve), (ownConcern, "r1", ReviewDecisionKind.RequestChanges), (otherConcern, "r2", ReviewDecisionKind.RequestChanges));

        await ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{approval}', 'r1', '{ownConcern}')");
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{approval}', 'r1', '{otherConcern}')"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task AResolutionCannotNameAnApprovalOfAnotherRecordBecauseTheForeignKeyIncludesTheRecord()
    {
        var (approval, concern) = (new string('a', 32), new string('c', 32));
        await InsertDecisionsAsync((approval, "r1", ReviewDecisionKind.Approve), (concern, "r2", ReviewDecisionKind.RequestChanges));

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO action_review_decision_resolutions (decision_id, record_id, resolved_decision_id) VALUES ('{approval}', 'r2', '{concern}')"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    // The read-only role hides a column by name, so each column that holds a reviewer or a note must match the pattern.
    [Fact]
    public async Task EveryColumnThatHoldsAnActorOrANoteIsHiddenFromTheReadOnlyRoleAndTheResolutionColumnsAreNot()
    {
        string[] hiddenColumns = ["actor_subject", "note", "correction_note"];
        var hidden = ReadOnlyGrants.HiddenColumn();
        var columns = await ReadAsync("""
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name ~ '_review_decision(s|_resolutions)$'
            """);

        Assert.All(hiddenColumns, hiddenColumn => Assert.Equal(7, columns.Count(entry => entry.EndsWith("." + hiddenColumn, StringComparison.Ordinal))));
        Assert.All(columns, entry =>
        {
            var column = entry[(entry.IndexOf('.') + 1)..];
            Assert.Equal(hiddenColumns.Contains(column), hidden.IsMatch(column));
        });
    }

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task TheDraftsHoldANullableRefusedCandidateWithAForeignKeyToTheCandidatesBecauseTheBlockMustNameARealUpdate(string prefix)
    {
        Assert.Equal(["YES"], await ReadAsync($"SELECT is_nullable FROM information_schema.columns WHERE table_name = '{prefix}_drafts' AND column_name = 'refused_candidate_id'"));
        Assert.Single(await ReadAsync($"""
            SELECT c.conname FROM pg_constraint c
            WHERE c.conrelid = '{prefix}_drafts'::regclass AND c.contype = 'f' AND c.confrelid = '{prefix}_candidates'::regclass
              AND c.conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '{prefix}_drafts'::regclass AND attname = 'refused_candidate_id')]
            """));
    }

    private Task InsertAsync(string prefix, string idColumn, ReviewDecisionKind kind, string? reasonCode, string? decisionId = null) =>
        InsertAsync(prefix, idColumn, kind.ToString(), reasonCode, decisionId);

    private async Task InsertDecisionsAsync(params (string DecisionId, string RecordId, ReviewDecisionKind Kind)[] decisions)
    {
        var rows = decisions.Select((decision, index) => $"('{decision.DecisionId}', '{decision.RecordId}', 1, {index + 1}, '{decision.Kind}', 'auth0|reviewer', 1)");
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
            VALUES {string.Join(", ", rows)}
            """);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertAsync(string prefix, string idColumn, string kind, string? reasonCode, string? decisionId = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SET session_replication_role = replica;
            INSERT INTO {prefix}_review_decisions (decision_id, {idColumn}, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks, reason_code)
            VALUES (@id, 'r1', 1, @version, @kind, 'auth0|reviewer', 1, @reason)
            """, connection);
        command.Parameters.AddWithValue("id", decisionId ?? new string('a', 32));
        command.Parameters.AddWithValue("version", ++version);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("reason", (object?)reasonCode ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string prefix)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {prefix}_review_decisions", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }
}
