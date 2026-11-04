using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RevisionColumnsReadOnlyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // The columns hide by name, so each text column of a revision must match the pattern and no other new column may.
    [Theory]
    [InlineData("vote_revisions,vote_revision_facts,vote_revision_issues,vote_member_lists,vote_member_list_entries",
        "author_subject,headline,summary,fact_text,suggestion_headline,suggestion_summary,suggestion_run_id,suggestion_fingerprint")]
    [InlineData("action_revisions,action_revision_issues,action_revision_sponsorships,action_revision_sponsorship_citations",
        "author_subject,revision_json,headline,summary,suggestion_headline,suggestion_summary,suggestion_run_id,suggestion_fingerprint")]
    public async Task EveryTextColumnOfARevisionIsHiddenFromTheReadOnlyRoleAndTheStructureColumnsAreNot(string tables, string hiddenColumns)
    {
        var expectedHidden = hiddenColumns.Split(',');
        var hidden = ReadOnlyGrants.HiddenColumn();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = ANY (@tables)", connection);
        command.Parameters.AddWithValue("tables", tables.Split(','));
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));

        Assert.All(expectedHidden, column => Assert.Contains(column, columns));
        Assert.All(columns, column => Assert.Equal(expectedHidden.Contains(column), hidden.IsMatch(column)));
    }
}
