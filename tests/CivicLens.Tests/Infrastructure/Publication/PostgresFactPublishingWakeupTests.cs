using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFactPublishingWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheDeadlineIsTheNextFullHourOfTheDatabaseClockNotOfTheApplicationClock()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT {PostgresFactPublishingWakeup.NextHourTicks},
                   (EXTRACT(EPOCH FROM date_trunc('hour', now()) + interval '1 hour')::numeric * 10000000 + 621355968000000000)::bigint
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal(reader.GetInt64(1), reader.GetInt64(0));
    }
}
