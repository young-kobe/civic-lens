using CivicLens.PublicApi.Schema;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Schema;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SchemaMigratorTests(PostgresCollection postgres)
{
    [Fact]
    public async Task TwoServicesThatStartTogetherApplyEachFileOnceAndRecordItInSchemaMigrations()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await using var first = NpgsqlDataSource.Create(connection);
            await using var second = NpgsqlDataSource.Create(connection);

            await Task.WhenAll(new SchemaMigrator(first).ApplyAsync(CancellationToken.None), new SchemaMigrator(second).ApplyAsync(CancellationToken.None));
            await new SchemaMigrator(first).ApplyAsync(CancellationToken.None);

            await using var command = first.CreateCommand("SELECT version || ':' || name FROM schema_migrations");
            Assert.Equal("1:001_search_index", await command.ExecuteScalarAsync());
        }
        finally
        {
            await postgres.DropDatabaseAsync(connection);
        }
    }
}
