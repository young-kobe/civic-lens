using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>The owner conversion of a retained release must write what a fresh publish writes, repeat without change, and be checked by the read-only verifier.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleasePartyLineUpBackfillTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private const string Republican = """('Republican', 'Republican', '2025-01-03', NULL)""";
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task BackfillWritesTheSameRowsThatTheFreshPublishWriterWroteForTheSameReleaseNames()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await SeedReleaseAsync(2, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString,
            Bill('1', "p1"), Bill('2', "p2"), Bill('3', "p2") with { Issues = ["health", "tax"] }, Bill('4', "ghost"),
            Bill('5', "p1") with { Sponsorships = [new("p1", "Sponsor", "2025-02-01"), new("p2", "Sponsor", "2025-02-01")] },
            Bill('6', "p1") with { Congress = 118 });
        await FillIssueCountsAsync(1);
        await FillIssueCountsAsync(2);
        await FillLineUpAsFreshPublishAsync(1);

        var (release, members) = await ReleaseAndMembersAsync(2);
        var added = await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);

        Assert.Equal(ReleaseSnapshotWrite.Added, added);
        Assert.Equal(["health|Democratic|2", "health|Republican|1", "tax|Democratic|1"], await PartiesAsync(1));
        Assert.Equal(["health|2", "tax|0"], await UnknownAsync(1));
        Assert.Equal(await PartiesAsync(1), await PartiesAsync(2));
        Assert.Equal(await UnknownAsync(1), await UnknownAsync(2));
    }

    [Fact]
    public async Task ARerunChangesNothingAndReportsNoWrite()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"), Bill('2', "ghost"));
        await FillIssueCountsAsync(1);
        var (release, members) = await ReleaseAndMembersAsync(1);

        var first = await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);
        var before = await SnapshotAsync(1);
        var replay = await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);

        Assert.Equal(ReleaseSnapshotWrite.Added, first);
        Assert.Equal(ReleaseSnapshotWrite.AlreadyPresent, replay);
        Assert.Equal(before, await SnapshotAsync(1));
        Assert.Equal(["health|Republican|1"], await PartiesAsync(1));
    }

    [Fact]
    public async Task APartialOrChangedSnapshotIsRefusedByTheBackfillAndIsNotRepaired()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"));
        await FillIssueCountsAsync(1);
        var (release, members) = await ReleaseAndMembersAsync(1);
        await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);
        await ExecuteAsync("DELETE FROM release_issue_party_counts WHERE release_number = 1");

        await Assert.ThrowsAsync<BillCountRefusedException>(() => publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default));

        Assert.Empty(await PartiesAsync(1));
    }

    [Theory]
    [InlineData("UPDATE release_issue_party_counts SET bills = 2 WHERE release_number = 1", BillCountStatus.DifferentRows)]
    [InlineData("UPDATE release_issue_party_counts SET party = 'Democratic' WHERE release_number = 1", BillCountStatus.DifferentRows)]
    [InlineData("UPDATE release_issue_party_lineups SET unknown_bills = 5 WHERE release_number = 1 AND issue_id = 'tax'", BillCountStatus.DifferentRows)]
    [InlineData("INSERT INTO release_issue_party_counts (release_number, issue_id, party, bills) VALUES (1, 'tax', 'Green', 1)", BillCountStatus.DifferentRows)]
    [InlineData("DELETE FROM release_issue_party_counts WHERE release_number = 1", BillCountStatus.DifferentRows)]
    [InlineData("DELETE FROM release_issue_party_counts WHERE release_number = 1; DELETE FROM release_issue_party_lineups WHERE release_number = 1", BillCountStatus.MissingRows)]
    public async Task VerifierFindsAForgedExtraOrMissingRowInBothDirections(string mutation, BillCountStatus expected)
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"));
        await FillIssueCountsAsync(1);
        var (release, members) = await ReleaseAndMembersAsync(1);
        await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);
        Assert.Equal(BillCountStatus.Verified, await publications.VerifyPartyLineUpsAsync(release, members, NoServedDirectory, default));
        await ExecuteAsync(mutation);

        Assert.Equal(expected, await publications.VerifyPartyLineUpsAsync(release, members, NoServedDirectory, default));
    }

    [Fact]
    public async Task VerifierOnAReleaseWithMissingRowsWritesNothing()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"));
        await FillIssueCountsAsync(1);
        var (release, members) = await ReleaseAndMembersAsync(1);

        var status = await publications.VerifyPartyLineUpsAsync(release, members, NoServedDirectory, default);

        Assert.Equal(BillCountStatus.MissingRows, status);
        Assert.Empty(await UnknownAsync(1));
        Assert.Empty(await PartiesAsync(1));
    }

    [Fact]
    public async Task AReleaseWithNoCountedIssueStoresNoRowsAndServesNoLineUpAndTheVerifierAcceptsIt()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"));
        var (release, members) = await ReleaseAndMembersAsync(1);

        var added = await publications.CommitPartyLineUpsAsync(release, members, NoServedDirectory, default);

        Assert.Equal(ReleaseSnapshotWrite.NothingExpected, added);
        Assert.Empty(await UnknownAsync(1));
        Assert.Empty(await Read("SELECT issue_id FROM public_api.release_issue_party_lineups"));
        Assert.Equal(BillCountStatus.NothingExpected, await publications.VerifyPartyLineUpsAsync(release, members, NoServedDirectory, default));
    }

    [Fact]
    public async Task ARowThatNoCountedIssueExpectsIsReportedAsDifferent()
    {
        await SeedReleaseAsync(1, "p1", "p2");
        await BillsSeed.AddBillsAsync(connectionString, Bill('1', "p1"));
        var (release, members) = await ReleaseAndMembersAsync(1);
        await ExecuteAsync("INSERT INTO release_issue_party_lineups (release_number, issue_id, unknown_bills) VALUES (1, 'health', 0)");

        Assert.Equal(BillCountStatus.DifferentRows, await publications.VerifyPartyLineUpsAsync(release, members, NoServedDirectory, default));
    }

    private static SeededBill Bill(char id, string sponsor) =>
        new(id, "2025-02-01") { BillNumber = id, Issues = ["health"], Sponsorships = [new(sponsor, "Sponsor", "2025-02-01")] };

    private static Task<string?> NoServedDirectory(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    private async Task SeedReleaseAsync(int number, params string[] officials)
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, number, Published.AddDays(number));
        await ExecuteAsync($"INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES ({number}, 'health', 'Health', 'Definition'), ({number}, 'tax', 'Tax', 'Definition')");
        foreach (var official in officials)
        {
            var term = official == "p2" ? """('Democratic', 'Democratic', '2025-01-03', NULL)""" : Republican;
            await ExecuteAsync($"""
                INSERT INTO release_officials (release_number, official_id, name) VALUES ({number}, '{official}', '{official}');
                INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
                VALUES ({number}, '{official}', 0, 'Representative', 'Representative', 'TX', 1, {term[1..]};
                """);
        }
    }

    private async Task<(PublicationReleaseSummary Release, PublishedRevisionBinding[] Members)> ReleaseAndMembersAsync(int number)
    {
        var members = (await Read($"SELECT record_id || '|' || revision_number FROM publication_record_states WHERE kind = 'action' AND first_release <= {number} AND (last_release IS NULL OR last_release >= {number}) ORDER BY record_id"))
            .Select(row => row.Split('|')).Select(parts => new PublishedRevisionBinding(PublishedRecordKind.Action, parts[0], int.Parse(parts[1]))).ToArray();
        return ((await publications.ListAllAsync(default)).Single(release => release.ReleaseNumber == number), members);
    }

    private Task FillIssueCountsAsync(int release) => InvokeAsync("ReleaseIssueCounts", release);

    private Task FillLineUpAsFreshPublishAsync(int release) => InvokeAsync("ReleaseIssuePartyCounts", release);

    private async Task InvokeAsync(string typeName, int release)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var type = typeof(PostgresPublicationStore).Assembly.GetType($"CivicLens.Infrastructure.Publication.{typeName}", throwOnError: true)!;
        await (Task)type.GetMethod("FillAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [db, release, (int?)119, CancellationToken.None])!;
        await transaction.CommitAsync();
    }

    private async Task<string[]> SnapshotAsync(int release) => [.. await UnknownAsync(release), .. await PartiesAsync(release)];

    private Task<string[]> PartiesAsync(int release) => Read($"SELECT issue_id || '|' || party || '|' || bills FROM release_issue_party_counts WHERE release_number = {release} ORDER BY issue_id, party");

    private Task<string[]> UnknownAsync(int release) => Read($"SELECT issue_id || '|' || unknown_bills FROM release_issue_party_lineups WHERE release_number = {release} ORDER BY issue_id");

    private Task ExecuteAsync(string sql) => ViewsSeed.ExecuteAsync(connectionString, sql);

    private async Task<string[]> Read(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
