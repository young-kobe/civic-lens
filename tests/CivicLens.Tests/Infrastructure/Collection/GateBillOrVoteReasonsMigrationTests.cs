using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class GateBillOrVoteReasonsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261028120000_VoteDrafting";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationLetsAGateRunKeepTheTwoBillOrVotePassReasonsAndLeavesNoPendingModelChanges()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.DoesNotContain("passed-bill-or-vote", await ReasonCheckAsync(), StringComparison.Ordinal);

        await db.Database.MigrateAsync();

        var check = await ReasonCheckAsync();
        Assert.Contains("passed-bill-or-vote", check, StringComparison.Ordinal);
        Assert.Contains("passed-bill-number", check, StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TheMigrationCanBeRolledBackSoADeploymentCanReturnToThePreviousSchema()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.DoesNotContain("passed-bill-number", await ReasonCheckAsync(), StringComparison.Ordinal);
    }

    private async Task<string> ReasonCheckAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_statement_analysis_runs_reason'", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
