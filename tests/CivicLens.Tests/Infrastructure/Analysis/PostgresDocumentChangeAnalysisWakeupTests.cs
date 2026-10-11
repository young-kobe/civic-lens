using System.Diagnostics;
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
public sealed class PostgresDocumentChangeAnalysisWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "analysis_wakeup_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-analysis-wakeup-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ComparisonSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        await attempts.MigrateAsync();
        seeder = new ComparisonSeeder(attempts, new PostgresDocumentExtractionStore(factory), new PostgresDocumentComparisonStore(factory),
            captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ANewlyQueuedChangeWakesTheListener()
    {
        await using var wakeup = PostgresDocumentChangeAnalysisWakeup.FromConnectionString(connectionString);
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
        await using var wakeup = PostgresDocumentChangeAnalysisWakeup.FromConnectionString(connectionString);
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

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
