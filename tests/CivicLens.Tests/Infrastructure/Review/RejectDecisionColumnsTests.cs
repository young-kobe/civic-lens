using CivicLens.Core.Review;
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

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task ARejectionWithoutAReasonCodeFailsTheDatabaseCheckOfEveryKindBecauseRejectionsAreCountedByCause(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, null));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code_kind", failure.ConstraintName);
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task AReasonCodeOnAnotherDecisionKindFailsTheDatabaseCheckOfEveryKind(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(prefix, idColumn, ReviewDecisionKind.RequestChanges, nameof(ReviewReasonCode.Duplicate)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code_kind", failure.ConstraintName);
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task AReasonCodeThatTheEnumLacksFailsTheDatabaseCheckOfEveryKind(string prefix, string idColumn)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, "Bogus"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal($"ck_{prefix}_review_decisions_reason_code", failure.ConstraintName);
    }

    [Theory]
    [InlineData("document_change", "draft_id")]
    [InlineData("statement", "candidate_id")]
    [InlineData("action", "record_id")]
    [InlineData("vote", "record_id")]
    [InlineData("executive_action", "record_id")]
    [InlineData("relationship", "record_id")]
    [InlineData("brief", "record_id")]
    public async Task EveryReasonCodeOfTheEnumIsStoredForARejectionOfEveryKind(string prefix, string idColumn)
    {
        foreach (var code in Enum.GetNames<ReviewReasonCode>())
            await InsertAsync(prefix, idColumn, ReviewDecisionKind.Reject, code, Guid.NewGuid().ToString("N"));

        Assert.Equal(Enum.GetNames<ReviewReasonCode>().Length, await CountAsync(prefix));
    }

    // The reason code names a cause and holds no person, so reports of rejections by cause may read it.
    [Fact]
    public async Task TheReasonCodeColumnIsNotHiddenFromTheReadOnlyRoleOfAnyDecisionTable()
    {
        var hidden = ReadOnlyHiddenColumns.Load();

        var tables = await ReadAsync("SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'reason_code' AND table_name ~ '_review_decisions$'");

        Assert.Equal(7, tables.Count);
        Assert.All(new[] { "actor_subject", "note" }, column => Assert.Matches(hidden, column));
        Assert.DoesNotMatch(hidden, "reason_code");
    }

    private async Task InsertAsync(string prefix, string idColumn, ReviewDecisionKind kind, string? reasonCode, string? decisionId = null)
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
        command.Parameters.AddWithValue("kind", kind.ToString());
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
