using System.Diagnostics;
using CivicLens.Infrastructure;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresIntervalWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const double SafeSecondsToTheBoundary = 10;
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheHourlyWakeupDoesNotReturnBeforeItsDeadlineBecauseNothingNotifiesIt() =>
        await AssertSleepsAsync(TimeSpan.FromHours(1));

    [Fact]
    public async Task TheFiveMinuteWakeupDoesNotReturnBeforeItsDeadlineBecauseNothingNotifiesIt() =>
        await AssertSleepsAsync(TimeSpan.FromMinutes(5));

    [Fact]
    public async Task ABurstOfApprovalNotificationsWakesOnceAtTheEarliestDeadlineBecauseTheDeadlineMustNotSlide()
    {
        if (await SecondsToTheNextBoundaryAsync(TimeSpan.FromHours(1)) < SafeSecondsToTheBoundary) return;
        await using var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_burst_test", TimeSpan.FromHours(1));
        await wakeup.ConnectAsync(default);
        var stopwatch = Stopwatch.StartNew();
        foreach (var seconds in new[] { 3.0, 6.0, 9.0 })
            await NotifyDeadlineAsync("civic_lens_burst_test", seconds);
        using var firstWake = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await wakeup.WaitAsync(firstWake.Token);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(2.9), TimeSpan.FromSeconds(5.5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ALostNotificationStillWakesAtTheIntervalDeadlineBecauseTheTimerIsTheFallback()
    {
        var interval = TimeSpan.FromSeconds(2);
        await using var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_lost_test", interval);
        await NotifyDeadlineAsync("civic_lens_lost_test", 0.1);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        await wakeup.WaitAsync(timeout.Token);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, interval + TimeSpan.FromSeconds(1));
    }

    private async Task NotifyDeadlineAsync(string channel, double secondsFromNow)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT pg_notify(@channel, ((EXTRACT(EPOCH FROM clock_timestamp())::numeric + @seconds) * 10000000 + 621355968000000000)::bigint::text)",
            connection);
        command.Parameters.AddWithValue("channel", channel);
        command.Parameters.AddWithValue("seconds", (decimal)secondsFromNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertSleepsAsync(TimeSpan interval)
    {
        if (await SecondsToTheNextBoundaryAsync(interval) < SafeSecondsToTheBoundary) return;
        await using var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_interval_test", interval);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }

    private async Task<double> SecondsToTheNextBoundaryAsync(TimeSpan interval)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (@seconds - mod(EXTRACT(EPOCH FROM clock_timestamp())::numeric, @seconds))::float8", connection);
        command.Parameters.AddWithValue("seconds", (decimal)interval.TotalSeconds);
        return (double)(await command.ExecuteScalarAsync())!;
    }
}
