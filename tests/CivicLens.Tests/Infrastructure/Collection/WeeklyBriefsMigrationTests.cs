using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class WeeklyBriefsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261025120000_DropReleaseCountCheck";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationAddsTheBriefTablesAndTheQueueWakeupTriggerAndLeavesNoPendingModelChanges()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name LIKE 'brief\\_%'"));

        await db.Database.MigrateAsync();

        Assert.Equal(7L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name LIKE 'brief\\_%'"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM pg_trigger WHERE tgrelid = 'brief_analysis'::regclass AND tgname = 'trg_brief_analysis_notify'"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheMigrationCanBeRolledBackSoADeploymentCanReturnToThePreviousSchema()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_name LIKE 'brief\\_%'"));
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
