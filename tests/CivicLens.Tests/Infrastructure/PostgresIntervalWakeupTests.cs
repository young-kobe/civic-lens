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
    public async Task TheWakeupSleepsUntilTheNextHourOfTheDatabaseClockBecauseNothingNotifiesIt() =>
        await AssertSleepsAsync(TimeSpan.FromHours(1));

    [Fact]
    public async Task TheWakeupSleepsUntilTheNextFiveMinuteMarkOfTheDatabaseClockBecauseNothingNotifiesIt() =>
        await AssertSleepsAsync(TimeSpan.FromMinutes(5));

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
