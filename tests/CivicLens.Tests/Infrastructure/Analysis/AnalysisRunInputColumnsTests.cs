using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AnalysisRunInputColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Table = "document_change_analysis_runs";
    private static readonly string[] RunTables =
    [
        "action_analysis_runs", "brief_analysis_runs", "document_change_analysis_runs", "exec_summary_analysis_runs", "relationship_analysis_runs",
        "statement_analysis_runs", "vote_analysis_runs"
    ];
    private static readonly string SystemHash = AnalysisRunInput.TextHash("System.");
    private static readonly string SchemaHash = AnalysisRunInput.TextHash("{}");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-run-input-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private string comparisonId = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        Directory.CreateDirectory(captureRoot);
        var seeder = new ComparisonSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
        comparisonId = (await seeder.SaveAsync("Text A.\n", "Text B.\n")).ComparisonId;
        await ExecuteAsync($$"""
            INSERT INTO analysis_system_prompts (text_sha256, text, created_at_utc_ticks) VALUES ('{{SystemHash}}', 'System.', 1);
            INSERT INTO analysis_output_schemas (text_sha256, text, created_at_utc_ticks) VALUES ('{{SchemaHash}}', '{}', 1)
            """);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task EveryRunTableGetsTheSameInputChecksAndTheTwoRestrictingTextKeysBecauseTheyShareOneConfiguration()
    {
        foreach (var table in RunTables)
        {
            var checks = await ReadAsync($"SELECT conname FROM pg_constraint WHERE conrelid = '{table}'::regclass AND contype = 'c' AND conname ~ '_(input|input_outcome|user_message|max_output_tokens)$' ORDER BY 1");
            Assert.Equal([$"ck_{table}_input", $"ck_{table}_input_outcome", $"ck_{table}_max_output_tokens", $"ck_{table}_user_message"], checks);
            var keys = await ReadAsync($"SELECT confrelid::regclass::text || ':' || confdeltype::text FROM pg_constraint WHERE conrelid = '{table}'::regclass AND contype = 'f' AND confrelid::regclass::text LIKE 'analysis_%' ORDER BY 1");
            Assert.Equal(["analysis_output_schemas:r", "analysis_system_prompts:r"], keys);
        }
    }

    [Fact]
    public async Task ARowWithAllFiveInputColumnsIsAcceptedAndARowWithNoneIsAcceptedBecauseOldRunsHoldNone()
    {
        await InsertRunAsync("Refused", "all");
        await InsertRunAsync("Refused", "none");

        Assert.Equal(["false", "true"], await ReadAsync($"SELECT (system_prompt_sha256 IS NULL)::text FROM {Table} ORDER BY 1"));
    }

    [Theory]
    [InlineData("user_message")]
    [InlineData("effort")]
    [InlineData("max_output_tokens")]
    [InlineData("system_prompt_sha256")]
    [InlineData("output_schema_sha256")]
    public async Task OneMissingInputColumnIsRefusedBecauseARunHoldsTheWholeInputOrNone(string missing)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Refused", "all", without: missing));

        Assert.Equal($"ck_{Table}_input", failure.ConstraintName);
    }

    [Fact]
    public async Task AnInterruptedRunCannotHoldAnInputBecauseTheRequestDiedWithTheWorker()
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Interrupted", "all"));

        Assert.Equal($"ck_{Table}_input_outcome", failure.ConstraintName);
        var runId = await InsertRunAsync("Interrupted", "none");
        Assert.Null(await StoredRunInputs.ReadAsync(connectionString, Table, runId));
    }

    [Fact]
    public async Task AnEmptyUserMessageAndANonPositiveTokenLimitAreRefused()
    {
        Assert.Equal($"ck_{Table}_user_message", (await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Refused", "all", userMessage: ""))).ConstraintName);
        Assert.Equal($"ck_{Table}_user_message", (await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Refused", "all",
            userMessage: new string('a', AnalysisRunInput.MaximumPromptBytes + 1)))).ConstraintName);
        Assert.Equal($"ck_{Table}_max_output_tokens", (await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Refused", "all", maximumTokens: 0))).ConstraintName);
    }

    [Theory]
    [InlineData("system_prompt_sha256")]
    [InlineData("output_schema_sha256")]
    public async Task ARunCannotNameATextThatHasNoRowBecauseTheKeysRestrict(string column)
    {
        var missing = AnalysisRunInput.TextHash("No such text.");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertRunAsync("Refused", "all", override_: (column, missing)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task ALegacyRunWithNoInputLoadsThroughTheReadRuleWithANullInput()
    {
        var runId = await InsertRunAsync("Refused", "none");

        var run = await new PostgresDocumentChangeAnalysisStore(factory).GetRunAsync(runId, default);

        Assert.NotNull(run);
        Assert.Null(run.Input);
        Assert.Equal(AnalysisRunOutcome.Refused, run.Outcome);
    }

    [Fact]
    public async Task ARunThatStoredItsInputReadsBackWithoutItBecauseNoReaderNeedsThePromptText()
    {
        var runId = await InsertRunAsync("Refused", "all");

        var run = await new PostgresDocumentChangeAnalysisStore(factory).GetRunAsync(runId, default);

        Assert.Null(run!.Input);
        Assert.Equal(new AnalysisRunInput("System.", "User message.", "{}", "medium", 100), await StoredRunInputs.ReadAsync(connectionString, Table, runId));
    }

    [Fact]
    public async Task TheReadOnlyRoleCannotReadTheUserMessageOfAnyRunTableButStillReadsTheSystemPromptTheSchemaAndTheFlagMessages()
    {
        await ExecuteAsync(string.Join('\n', File.ReadAllLines(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql"))
            .Where(line => !line.StartsWith('\\'))));

        foreach (var table in RunTables)
        {
            Assert.Equal("false", (await ReadAsync($"SELECT has_column_privilege('civic_lens_readonly', '{table}', 'user_message', 'SELECT')::text"))[0]);
            Assert.Equal("true", (await ReadAsync($"SELECT has_column_privilege('civic_lens_readonly', '{table}', 'output_json', 'SELECT')::text"))[0]);
        }
        Assert.Equal("true", (await ReadAsync("SELECT has_column_privilege('civic_lens_readonly', 'analysis_system_prompts', 'text', 'SELECT')::text"))[0]);
        Assert.Equal("true", (await ReadAsync("SELECT has_column_privilege('civic_lens_readonly', 'analysis_output_schemas', 'text', 'SELECT')::text"))[0]);
        Assert.Equal("true", (await ReadAsync("SELECT has_column_privilege('civic_lens_readonly', 'brief_check_flags', 'message', 'SELECT')::text"))[0]);
    }

    private async Task<string> InsertRunAsync(string outcome, string input, string? without = null, string? userMessage = null,
        int maximumTokens = 100, (string Column, string Value)? override_ = null)
    {
        var runId = Guid.NewGuid().ToString("N");
        var values = new Dictionary<string, string?>
        {
            ["system_prompt_sha256"] = input == "all" ? SystemHash : null,
            ["output_schema_sha256"] = input == "all" ? SchemaHash : null,
            ["user_message"] = input == "all" ? userMessage ?? "User message." : null,
            ["effort"] = input == "all" ? "medium" : null,
            ["max_output_tokens"] = input == "all" ? maximumTokens.ToString() : null
        };
        if (without is not null) values[without] = null;
        if (override_ is { } replaced) values[replaced.Column] = replaced.Value;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {Table} (run_id, comparison_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
                input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks,
                context_json, system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens)
            VALUES (@run, @comparison, 'draft', 'v', 'model', 'p', 's', '{new string('1', 64)}', @outcome, 0, 0, 0, 0, 0, 0, 0,
                @context, @system, @schema, @user, @effort, @max)
            """, connection);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("comparison", comparisonId);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("context", outcome == "Interrupted" ? DBNull.Value : "{}");
        command.Parameters.AddWithValue("system", (object?)values["system_prompt_sha256"] ?? DBNull.Value);
        command.Parameters.AddWithValue("schema", (object?)values["output_schema_sha256"] ?? DBNull.Value);
        command.Parameters.AddWithValue("user", (object?)values["user_message"] ?? DBNull.Value);
        command.Parameters.AddWithValue("effort", (object?)values["effort"] ?? DBNull.Value);
        command.Parameters.AddWithValue("max", values["max_output_tokens"] is { } tokens ? int.Parse(tokens) : DBNull.Value);
        _ = await command.ExecuteNonQueryAsync();
        return runId;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
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
