using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFactPublishingWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const double SafeSecondsToTheHour = 10;
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheWakeupSleepsUntilTheNextHourOfTheDatabaseClockBecauseNothingNotifiesIt()
    {
        if (await SecondsToTheNextHourAsync() < SafeSecondsToTheHour) return;
        await using var wakeup = PostgresFactPublishingWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }

    private async Task<double> SecondsToTheNextHourAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXTRACT(EPOCH FROM date_trunc('hour', clock_timestamp()) + interval '1 hour' - clock_timestamp())::float8", connection);
        return (double)(await command.ExecuteScalarAsync())!;
    }
}
