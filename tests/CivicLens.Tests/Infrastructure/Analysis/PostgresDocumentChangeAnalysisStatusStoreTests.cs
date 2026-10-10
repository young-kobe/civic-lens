using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeAnalysisStatusStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "analysis_status_" + Guid.NewGuid().ToString("N");
    private PostgresDocumentChangeAnalysisStatusStore status = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
        status = PostgresDocumentChangeAnalysisStatusStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");

    [Fact]
    public async Task TheWorkerReportsWhetherDraftingIsOnAndItsDailyLimit()
    {
        Assert.Null(await status.GetAsync(default));

        await status.RecordStartAsync(50_000, default);
        var on = (await status.GetAsync(default))!;
        Assert.Equal((true, 50_000L), (on.Enabled, on.DailyTokenLimit));
        Assert.InRange(DateTimeOffset.UtcNow - on.RecordedAtUtc, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        await status.RecordStartAsync(null, default);
        Assert.False((await status.GetAsync(default))!.Enabled);
    }

    [Fact]
    public async Task AnOutagePauseClearsAndARestartClearsAnyPause()
    {
        await status.RecordStartAsync(50_000, default);
        await status.RecordPauseAsync("providerOutage", TimeSpan.FromMinutes(5), default);
        var paused = (await status.GetAsync(default))!;
        Assert.True(paused.IsPaused(DateTimeOffset.UtcNow));
        Assert.False(paused.IsPaused(paused.PausedUntilUtc!.Value.AddSeconds(1)));

        await status.ClearOutagePauseAsync(default);
        Assert.Null((await status.GetAsync(default))!.PausedReason);

        await status.RecordPauseAsync("authenticationFailed", null, default);
        await status.RecordStartAsync(50_000, default);
        Assert.Null((await status.GetAsync(default))!.PausedReason);
    }

    [Fact]
    public async Task AnOutageNeverReplacesOrClearsAPauseThatWaitsForARestart()
    {
        await status.RecordStartAsync(50_000, default);
        await status.RecordPauseAsync("billingFailed", null, default);

        await status.RecordPauseAsync("providerOutage", TimeSpan.FromMinutes(5), default);
        await status.ClearOutagePauseAsync(default);

        var paused = (await status.GetAsync(default))!;
        Assert.Equal(("billingFailed", (DateTimeOffset?)null), (paused.PausedReason, paused.PausedUntilUtc));
        Assert.True(paused.IsPaused(DateTimeOffset.UtcNow.AddYears(1)));
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
