using CivicLens.Infrastructure.Analysis;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresAnalysisStatusStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresAnalysisStatusStore status = null!;
    private PostgresAnalysisStatusStore gateStatus = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        status = PostgresAnalysisStatusStore.FromConnectionString(connectionString);
        gateStatus = PostgresAnalysisStatusStore.FromConnectionString(connectionString, "gate");
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

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
    public async Task TheGateKeepsItsOwnStatusAndPauseBesideDrafting()
    {
        await status.RecordStartAsync(50_000, default);
        await gateStatus.RecordStartAsync(7_000, default);

        await gateStatus.RecordPauseAsync("issuesConfigurationUnset", null, default);

        Assert.Null((await status.GetAsync(default))!.PausedReason);
        var gate = (await gateStatus.GetAsync(default))!;
        Assert.Equal((7_000L, "issuesConfigurationUnset"), (gate.DailyTokenLimit, gate.PausedReason));
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
}
