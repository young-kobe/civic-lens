using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AnalysisPromptTextsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task RunsWithTheSameSystemPromptAndSchemaShareOneRowAndTheFirstWriterKeepsItsTime()
    {
        await EnsureAsync(Input("System A.", "First question.", "{}"), nowTicks: 100);
        await EnsureAsync(Input("System A.", "Second question.", "{}"), nowTicks: 200);

        Assert.Equal(["System A.|100"], await ReadAsync("SELECT text || '|' || created_at_utc_ticks FROM analysis_system_prompts"));
        Assert.Equal(["{}|100"], await ReadAsync("SELECT text || '|' || created_at_utc_ticks FROM analysis_output_schemas"));
    }

    [Fact]
    public async Task TheUserMessageIsNeverStoredInATextTableBecauseItBelongsToOneRun()
    {
        await EnsureAsync(Input("System A.", "Private question.", "{}"), nowTicks: 1);

        Assert.Empty(await ReadAsync("SELECT text FROM analysis_system_prompts WHERE text LIKE '%Private%' UNION ALL SELECT text FROM analysis_output_schemas WHERE text LIKE '%Private%'"));
    }

    [Fact]
    public async Task ParallelWritersOfOverlappingTextsNeverDeadlockOrDuplicateARow()
    {
        var inputs = Enumerable.Range(0, 48).Select(index => Input($"System {index % 4}.", "User.", $$"""{"schema":{{index % 3}}}""")).ToArray();

        await Task.WhenAll(inputs.Select(input => Task.Run(() => EnsureAsync(input, nowTicks: 1))));

        Assert.Equal(["System 0.", "System 1.", "System 2.", "System 3."], await ReadAsync("SELECT text FROM analysis_system_prompts ORDER BY text"));
        Assert.Equal(3, (await ReadAsync("SELECT text FROM analysis_output_schemas")).Length);
    }

    // The system prompt row goes first so every writer takes the two table locks in one order.
    [Fact]
    public async Task TheSystemPromptIsInsertedBeforeTheOutputSchema()
    {
        var input = Input("System first.", "User.", """{"schema":"second"}""");
        await using var holder = new NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var holdTransaction = await holder.BeginTransactionAsync();
        await ExecuteAsync(holder, $"INSERT INTO analysis_output_schemas (text_sha256, text, created_at_utc_ticks) VALUES ('{input.OutputSchemaSha256}', '{input.OutputSchema}', 1)");

        var writer = Task.Run(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await AnalysisPromptTexts.EnsureAsync(db, input, 2, default);
            await transaction.CommitAsync();
        });
        await WaitUntilBlockedAsync();

        await using var probe = new NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        await ExecuteAsync(probe, "SET lock_timeout = '300ms'");
        var timeout = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(probe,
            $"INSERT INTO analysis_system_prompts (text_sha256, text, created_at_utc_ticks) VALUES ('{input.SystemPromptSha256}', 'System first.', 3) ON CONFLICT DO NOTHING"));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, timeout.SqlState);

        await holdTransaction.CommitAsync();
        await writer;
        Assert.Equal(["System first."], await ReadAsync("SELECT text FROM analysis_system_prompts"));
    }

    [Theory]
    [InlineData("analysis_system_prompts")]
    [InlineData("analysis_output_schemas")]
    public async Task TheDatabaseRefusesATextWhoseKeyIsNotItsSha256BecauseTheKeyIsTheOnlyProofThatTheTextIsTheOneRunsUsed(string table)
    {
        var wrongKey = AnalysisRunInput.TextHash("Another text.");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(table, wrongKey, "This text."));

        Assert.Equal($"ck_{table}_hash", failure.ConstraintName);
        await InsertAsync(table, AnalysisRunInput.TextHash("This text."), "This text.");
        await InsertAsync(table, AnalysisRunInput.TextHash("Café ☕ <&>"), "Café ☕ <&>");
    }

    [Theory]
    [InlineData("analysis_system_prompts")]
    [InlineData("analysis_output_schemas")]
    public async Task TheDatabaseRefusesAnUppercaseKeyAndATextOutsideTheLengthRange(string table)
    {
        var upper = AnalysisRunInput.TextHash("This text.").ToUpperInvariant();
        var tooLong = new string('a', AnalysisRunInput.MaximumPromptBytes + 1);

        Assert.Equal($"ck_{table}_hash", (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(table, upper, "This text."))).ConstraintName);
        Assert.Equal($"ck_{table}_text", (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(table, AnalysisRunInput.TextHash(""), ""))).ConstraintName);
        Assert.Equal($"ck_{table}_text", (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(table, AnalysisRunInput.TextHash(tooLong), tooLong))).ConstraintName);
    }

    private static AnalysisRunInput Input(string system, string user, string schema) => new(system, user, schema, "medium", 100);

    private async Task EnsureAsync(AnalysisRunInput input, long nowTicks)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AnalysisPromptTexts.EnsureAsync(db, input, nowTicks, default);
        await transaction.CommitAsync();
    }

    private async Task InsertAsync(string table, string key, string text)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"INSERT INTO {table} (text_sha256, text, created_at_utc_ticks) VALUES (@key, @text, 1)", connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("text", text);
        _ = await command.ExecuteNonQueryAsync();
    }

    private async Task WaitUntilBlockedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if ((await ReadAsync("SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")).Length > 0) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The writer never waited for the output schema lock.");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        _ = await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }
}
