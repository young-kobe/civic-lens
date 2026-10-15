using System.Diagnostics;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresAnalysisWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-analysis-wakeup-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ComparisonSeeder seeder = null!;
    private StatementCandidateSeeder statements = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        var extractions = new PostgresDocumentExtractionStore(factory);
        seeder = new ComparisonSeeder(attempts, extractions, new PostgresDocumentComparisonStore(factory), captureRoot);
        statements = new StatementCandidateSeeder(attempts, extractions, connectionString, captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ANewlyQueuedChangeWakesTheListener()
    {
        await using var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftTask.Task);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = wakeup.WaitAsync(timeout.Token);

        await seeder.SaveAsync("Text A.\n", "Text B.\n");

        await waiting;
    }

    [Fact]
    public async Task ALeaseDeadlineWakesTheListenerWithoutANotification()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        await ExecuteAsync(connectionString, $"""
            UPDATE document_change_analysis SET status = 'Running', lease_token = 'lease', lease_expires_at = 9000000000000000000
            WHERE comparison_id = '{comparison.ComparisonId}'
            """);
        await using var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftTask.Task);
        await wakeup.ConnectAsync(default);
        await ExecuteAsync(connectionString, $"""
            UPDATE document_change_analysis SET lease_expires_at =
                (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968001000000)::bigint
            WHERE comparison_id = '{comparison.ComparisonId}'
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        await wakeup.WaitAsync(timeout.Token);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ARetryDeadlineWakesOnlyTheListenerForItsOwnTask()
    {
        var candidateId = await statements.SeedAsync("Press release.", "https://example.test/pages/article");
        await ExecuteAsync(connectionString, $"""
            UPDATE statement_analysis SET status = 'RetryWaiting', retry_at =
                (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968010000000)::bigint
            WHERE candidate_id = '{candidateId}' AND task = '{StatementGateTask.Task}'
            """);
        await using var gate = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementGateTask.Task);
        await using var drafting = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementDraftTask.Task);
        await gate.ConnectAsync(default);
        await drafting.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await gate.WaitAsync(timeout.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drafting.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ANotificationWakesOnlyTheListenerForItsOwnQueue()
    {
        await using var gate = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementGateTask.Task);
        await using var drafting = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementDraftTask.Task);
        await gate.ConnectAsync(default);
        await drafting.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var gateWaiting = gate.WaitAsync(timeout.Token);
        var draftingWaiting = drafting.WaitAsync(timeout.Token);

        await ExecuteAsync(connectionString,
            $"SELECT pg_notify('civic_lens_analysis', '{PostgresStatementAnalysisStore.Table}:{StatementGateTask.Task}')");

        await gateWaiting;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draftingWaiting);
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
