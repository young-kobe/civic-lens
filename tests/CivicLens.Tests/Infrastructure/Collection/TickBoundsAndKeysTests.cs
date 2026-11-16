using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TickBoundsAndKeysTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private const long LastTick = 3155378975999999999;
    private static readonly string Hash = new('a', 64);
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // One database holds all the checks, so one loop proves that every one of them uses the same range and the same null rule.
    [Fact]
    public async Task EveryTickColumnRefusesAValueBeyondTheTimeRangeAndAcceptsTheEdgesAndNull()
    {
        var columns = await ReadRowsAsync("""
            SELECT c.relname, a.attname, k.conname, pg_get_constraintdef(k.oid)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid AND c.relkind = 'r' AND c.relnamespace = current_schema()::regnamespace
            LEFT JOIN pg_constraint k ON k.conrelid = c.oid AND k.contype = 'c' AND k.conkey = ARRAY[a.attnum] AND pg_get_constraintdef(k.oid) LIKE '%3155378975999999999%'
            WHERE a.attname LIKE '%\_utc\_ticks' AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY 1, 2
            """);
        Assert.True(columns.Count >= 70, "The migrated database must hold the tick columns of the operational tables.");

        await using var connection = await OpenAsync();
        foreach (var (table, column, constraint, definition) in columns.Select(row => (row[0], row[1], row[2], row[3])))
        {
            Assert.True(constraint.Length > 0, $"{table}.{column} has no range check.");
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS tick_probe; CREATE TEMP TABLE tick_probe ({column} bigint); ALTER TABLE tick_probe ADD {definition}");
            Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO tick_probe VALUES (-1)"))).SqlState);
            Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO tick_probe VALUES ({LastTick + 1})"))).SqlState);
            await ExecuteAsync(connection, $"INSERT INTO tick_probe VALUES (0), ({LastTick}), (NULL)");
        }
    }

    [Theory]
    [InlineData("evidence_processing", "extraction_id", "document_extractions")]
    [InlineData("statement_gate_labels", "extraction_id", "document_extractions")]
    [InlineData("evidence_processing", "comparison_id", "document_comparisons")]
    [InlineData("vote_drafts", "candidate_id", "vote_candidates")]
    [InlineData("action_drafts", "candidate_id", "action_candidates")]
    [InlineData("action_analysis", "retry_of_run_id", "action_analysis_runs")]
    [InlineData("brief_analysis", "retry_of_run_id", "brief_analysis_runs")]
    [InlineData("document_change_analysis", "retry_of_run_id", "document_change_analysis_runs")]
    [InlineData("relationship_analysis", "retry_of_run_id", "relationship_analysis_runs")]
    [InlineData("statement_analysis", "retry_of_run_id", "statement_analysis_runs")]
    [InlineData("vote_analysis", "retry_of_run_id", "vote_analysis_runs")]
    public async Task AKeyedColumnRefusesAValueThatNoParentRowHoldsAndNeverDeletesAParentWithChildren(string table, string column, string parent)
    {
        var definition = Assert.Single(await ReadAsync($"""
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE contype = 'f' AND conrelid = '{table}'::regclass AND confrelid = '{parent}'::regclass
              AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '{table}'::regclass AND attname = '{column}')]
            """));
        Assert.EndsWith("ON DELETE RESTRICT", definition);

        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"CREATE TABLE key_probe ({column} varchar(64)); ALTER TABLE key_probe ADD {definition}");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO key_probe VALUES ('{Hash}')"));
        Assert.Equal(ForeignKeyViolation, failure.SqlState);
        await ExecuteAsync(connection, "INSERT INTO key_probe VALUES (NULL)");
    }

    // The processing store reads the extraction before it writes the id, so a record never points to a missing extraction.
    [Fact]
    public async Task AnEvidenceRecordWithAnExtractionThatDoesNotExistIsRefused()
    {

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertEvidenceProcessingAsync("j1", extractionId: Hash));

        Assert.Equal(ForeignKeyViolation, failure.SqlState);
    }

    // The reservation names its run before that run row exists, so a key on pending_run_id would refuse every reservation.
    [Theory]
    [InlineData("action_analysis")]
    [InlineData("brief_analysis")]
    [InlineData("document_change_analysis")]
    [InlineData("relationship_analysis")]
    [InlineData("statement_analysis")]
    [InlineData("vote_analysis")]
    public async Task ThePendingRunOfAReservationHasNoKeyBecauseItsRunRowIsWrittenLater(string table)
    {

        Assert.Empty(await ReadAsync($"""
            SELECT k.conname FROM pg_constraint k JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = ANY (k.conkey)
            WHERE k.contype = 'f' AND k.conrelid = '{table}'::regclass AND a.attname = 'pending_run_id'
            """));
    }

    // The predecessor can be an attempt that has no row, and attempt_id alone is not unique in evidence_processing, so no key can hold it.
    [Fact]
    public async Task ThePredecessorAttemptOfAnEvidenceRecordHasNoKeyBecauseThePredecessorMayHaveNoRow()
    {

        Assert.Empty(await ReadAsync("""
            SELECT k.conname FROM pg_constraint k JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = ANY (k.conkey)
            WHERE k.contype = 'f' AND k.conrelid = 'evidence_processing'::regclass AND a.attname = 'predecessor_attempt_id'
            """));
    }

    private Task InsertEvidenceProcessingAsync(string jobId, string? extractionId = null) => ExecuteAsync($"""
        INSERT INTO evidence_processing (job_id, attempt_id, source_id, requested_url, stage, status, fence, attempts, extraction_id,
            admitted_count, deferred_count, duplicate_count)
        VALUES ('{jobId}', 'a1', 's1', 'https://example.test/', 'Extraction', 'Pending', 0, 0, {(extractionId is null ? "NULL" : $"'{extractionId}'")}, 0, 0, 0)
        """);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<List<string>> ReadAsync(string sql) => [.. (await ReadRowsAsync(sql)).Select(row => row[0])];

    private async Task<List<string[]>> ReadRowsAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "" : reader.GetValue(index).ToString()!)]);
        return rows;
    }
}
