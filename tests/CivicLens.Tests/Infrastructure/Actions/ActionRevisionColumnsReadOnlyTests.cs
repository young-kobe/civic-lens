using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Actions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionRevisionColumnsReadOnlyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string[] Tables =
        ["action_revisions", "action_revision_issues", "action_revision_sponsorships", "action_revision_sponsorship_citations"];

    // The columns hide by name, so each text column of a revision must match the pattern and no other new column may.
    private static readonly string[] HiddenColumns =
        ["author_subject", "revision_json", "headline", "summary", "suggestion_headline", "suggestion_summary", "suggestion_run_id", "suggestion_fingerprint"];

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task EveryTextColumnOfAnActionRevisionIsHiddenFromTheReadOnlyRoleAndTheStructureColumnsAreNot()
    {
        var hidden = ReadOnlyGrants.HiddenColumn();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = ANY (@tables)", connection);
        command.Parameters.AddWithValue("tables", Tables);
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));

        Assert.All(HiddenColumns, column => Assert.Contains(column, columns));
        Assert.All(columns, column => Assert.Equal(HiddenColumns.Contains(column), hidden.IsMatch(column)));
    }
}
