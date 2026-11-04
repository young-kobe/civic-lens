using CivicLens.Tests.Fixtures;
using System.Diagnostics;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresPublishingWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string DirectorySuffix = "0123456789abcdef0123456789abcdef";
    private const double SafeSecondsToTheBoundary = 10;
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheAgeOfTheNewestReleaseComesFromTheNewestStoredTimeNotTheHighestNumberBecauseTheGapFollowsTheLastPublish()
    {
        var store = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
        Assert.Null(await store.ReadAgeOfNewestReleaseAsync(default));
        await InsertReleaseAsync(1, TimeSpan.FromSeconds(30));
        await InsertReleaseAsync(2, TimeSpan.FromHours(1));

        var age = await store.ReadAgeOfNewestReleaseAsync(default);

        Assert.InRange(age!.Value, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));
    }

    [Fact]
    public async Task TheWakeupFiresWhenTheGapAfterTheNewestReleaseEndsAndAFreshWakeupAfterARestartStillFiresThere()
    {
        if (await SecondsToTheNextFullHourAsync() < SafeSecondsToTheBoundary) return;
        var gap = TimeSpan.FromSeconds(4);
        await InsertReleaseAsync(1, TimeSpan.FromHours(1));
        await InsertReleaseAsync(2, TimeSpan.Zero);
        var stopwatch = Stopwatch.StartNew();
        await using (var beforeRestart = PostgresPublishingWakeup.FromConnectionString(connectionString, gap))
            await beforeRestart.ConnectAsync(default);
        await using var afterRestart = PostgresPublishingWakeup.FromConnectionString(connectionString, gap);
        await afterRestart.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await afterRestart.WaitAsync(timeout.Token);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6));
    }

    private async Task InsertReleaseAsync(int number, TimeSpan age)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (@number, '{number:D6}-{DirectorySuffix}', 'owner',
                (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint - @age, 1, @active)
            """, connection);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("age", age.Ticks);
        command.Parameters.AddWithValue("active", number == 2);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<double> SecondsToTheNextFullHourAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (3600 - mod(EXTRACT(EPOCH FROM clock_timestamp())::numeric, 3600))::float8", connection);
        return (double)(await command.ExecuteScalarAsync())!;
    }
}
