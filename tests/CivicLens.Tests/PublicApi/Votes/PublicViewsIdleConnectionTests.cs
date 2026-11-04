using System.Net;
using CivicLens.PublicApi;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicViewsIdleConnectionTests(PostgresCollection postgres)
{
    private const int RoleIdleSessionSeconds = 30;

    // The test fixture turns pooling off for its own connections. The service needs its pool, as in production.
    private static PublicApiOptions PooledViews(PublicApiOptions options) => options with
    {
        PublicViewsIdleLifetime = TimeSpan.FromSeconds(10),
        PublicViewsDatabase = new NpgsqlConnectionStringBuilder(options.PublicViewsDatabase) { Pooling = true }.ConnectionString
    };

    // The pool prunes every 10 seconds, so the 10 second minimum lifetime closes the connection within 20 seconds, before the role does.
    [Fact]
    public async Task ThePoolClosesAnIdleConnectionBeforeTheRoleEndsItsSessionSoTheNextRequestNeverMeetsAClosedConnection()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), PooledViews);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('a', "2026-03-02"));
        await ShortenRoleIdleTimeoutAsync(host.OperationalDatabase);
        using var warmUp = await host.Client.GetAsync("/v1/release");
        Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(RoleIdleSessionSeconds + 1));

        using var afterQuiet = await host.Client.GetAsync("/v1/release");
        Assert.Equal(HttpStatusCode.OK, afterQuiet.StatusCode);
    }

    // The setting binds to the test database only, so no other test sees it.
    private static async Task ShortenRoleIdleTimeoutAsync(string operationalDatabase)
    {
        var name = new NpgsqlConnectionStringBuilder(operationalDatabase).Database;
        await using var connection = new NpgsqlConnection(operationalDatabase);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"ALTER ROLE civic_lens_public IN DATABASE \"{name}\" SET idle_session_timeout = '{RoleIdleSessionSeconds}s'", connection);
        await command.ExecuteNonQueryAsync();
    }
}
