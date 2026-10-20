using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFactPublishingWakeupTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheWakeupSleepsUntilTheNextHourBecauseNothingNotifiesIt()
    {
        await using var wakeup = PostgresFactPublishingWakeup.FromConnectionString(connectionString);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }
}
