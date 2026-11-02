using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteRevisionColumnsReadOnlyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string[] Tables =
        ["vote_revisions", "vote_revision_facts", "vote_revision_issues", "vote_member_lists", "vote_member_list_entries"];

    // The columns hide by name, so each text column of a revision must match the pattern and no other new column may.
    private static readonly string[] HiddenColumns =
        ["author_subject", "revision_json", "headline", "summary", "fact_text", "suggestion_headline", "suggestion_summary", "suggestion_run_id", "suggestion_fingerprint"];

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task EveryTextColumnOfAVoteRevisionIsHiddenFromTheReadOnlyRoleAndTheStructureColumnsAreNot()
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
