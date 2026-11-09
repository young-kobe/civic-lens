using System.Reflection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseIssueCountsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task FillUsesReleaseRevisionRangesAndEligibilityThenKeepsFilledRowsImmutable()
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, Published);
        await ViewsSeed.ActivateReleaseAsync(connectionString, 2, Published.AddDays(1));
        await ViewsSeed.AddVotesAsync(connectionString,
            new SeededVote('a', "2026-05-01") { Issues = ["health"] },
            new SeededVote('b', "2026-05-02") { Kind = "Rule", Issues = ["health"] },
            new SeededVote('c', "2026-05-03") { Congress = 118, Issues = ["health"] },
            new SeededVote('d', "2026-05-04") { Kind = "Nomination", Chamber = "Senate", ActiveRevision = 2, EarlierIssues = ["health"], Issues = ["climate"] },
            new SeededVote('e', "2026-05-05") { UntrackedIssues = ["health"] },
            new SeededVote('f', "2026-05-06") { Issues = ["health"], LastRelease = 1 },
            new SeededVote('0', "2026-05-07") { Issues = ["health"], FirstRelease = 2 });
        await BillsSeed.AddBillsAsync(connectionString,
            new SeededBill('1', "2026-05-01") { Issues = ["health"] },
            new SeededBill('2', "2026-05-02") { Congress = 118, Issues = ["health"] },
            new SeededBill('3', "2026-05-03") { Issues = ["health"], FirstRelease = 2 },
            new SeededBill('4', "2026-05-04") { Issues = ["health"], LastRelease = 1 },
            new SeededBill('5', "2026-05-05") { Issues = ["health"], Sponsorships = [] },
            new SeededBill('6', "2026-05-06") { UntrackedIssues = ["health"] });
        await InsertIssueNamesAsync();

        Assert.Equal(3, await FillAsync(1, 119));
        Assert.Equal(["climate|119|1|0", "empty|119|0|0", "health|119|2|2"], await ReadCountsAsync(1));

        await ViewsSeed.ExecuteAsync(connectionString, """
            UPDATE vote_revision_issues SET is_tracked = false WHERE record_id = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
            UPDATE action_revision_issues SET is_tracked = false WHERE record_id = '1111111111111111111111111111111111111111111111111111111111111111';
            """);
        Assert.Equal(0, await FillAsync(1, 119));
        Assert.Equal(["climate|119|1|0", "empty|119|0|0", "health|119|2|2"], await ReadCountsAsync(1));

        Assert.Equal(3, await FillAsync(2, 119));
        Assert.Equal(["climate|119|1|0", "empty|119|0|0", "health|119|1|1"], await ReadCountsAsync(2));
        Assert.Equal(0, await FillAsync(3, 119));
    }

    private async Task InsertIssueNamesAsync() => await ViewsSeed.ExecuteAsync(connectionString, """
        INSERT INTO release_issues (release_number, issue_id, name, definition)
        VALUES (1, 'health', 'Health', 'Definition'), (1, 'climate', 'Climate', 'Definition'), (1, 'empty', 'Empty', 'Definition'),
               (2, 'health', 'Health', 'Definition'), (2, 'climate', 'Climate', 'Definition'), (2, 'empty', 'Empty', 'Definition');
        """);

    private async Task<int> FillAsync(int releaseNumber, int congress)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var type = typeof(PostgresPublicationStore).Assembly.GetType("CivicLens.Infrastructure.Publication.ReleaseIssueCounts", throwOnError: true)!;
        var method = type.GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task<int>)method.Invoke(null, [db, releaseNumber, (int?)congress, CancellationToken.None])!;
        return await task;
    }

    private async Task<string[]> ReadCountsAsync(int releaseNumber)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT issue_id || '|' || count_congress || '|' || substantive_votes || '|' || bills FROM release_issues WHERE release_number = @release ORDER BY issue_id", connection);
        command.Parameters.AddWithValue("release", releaseNumber);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
