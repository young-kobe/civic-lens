using System.Collections.Immutable;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseNamesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;
    private int commitCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACommitWritesTheOfficialsTermsIssuesAndPolicyAreasOfItsReleaseWithTheDefinitionInColumns()
    {
        await CommitAsync(1, null, Names("Rent and home prices.", ["Rent caps", "Eviction rules"], ["Zoning votes"]));

        Assert.Equal(["1|mayor|Mayor Example|Q1"], await ReadAsync("SELECT release_number || '|' || official_id || '|' || name || '|' || wikidata_id FROM release_officials"));
        Assert.Equal(["1|mayor|0|Mayor|Executive||||2024-01-01|", "1|mayor|1|Senator|Senator|WA||Democratic|2020-01-03|2023-01-03"],
            await ReadAsync("""
                SELECT release_number || '|' || official_id || '|' || "position" || '|' || office || '|' || kind || '|' || coalesce(state, '') || '|' || coalesce(district::text, '')
                    || '|' || coalesce(party, '') || '|' || start_on || '|' || coalesce(end_on::text, '')
                FROM release_official_terms ORDER BY "position"
                """));
        Assert.Equal(["1|housing|Housing costs|Rent and home prices."], await ReadAsync("SELECT release_number || '|' || issue_id || '|' || name || '|' || definition FROM release_issues"));
        Assert.Equal(["0|0"], await ReadAsync("SELECT substantive_votes || '|' || bills FROM release_issues WHERE release_number = 1"));
        Assert.Equal(["1|housing|0"], await ReadAsync("SELECT release_number || '|' || issue_id || '|' || unknown_bills FROM release_issue_party_lineups"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_issue_party_counts"));
        Assert.Equal(["0 Rent caps", "1 Eviction rules"], await ReadAsync("""SELECT "position" || ' ' || text FROM release_issue_includes ORDER BY "position" """));
        Assert.Equal(["0 Zoning votes"], await ReadAsync("""SELECT "position" || ' ' || text FROM release_issue_excludes"""));
        Assert.Equal(["1|land-use|Land use"], await ReadAsync("SELECT release_number || '|' || policy_area_id || '|' || name FROM release_policy_areas"));
    }

    [Fact]
    public async Task ALaterReleaseWritesItsOwnRowsAndLeavesTheRowsOfTheEarlierReleaseAsTheyWereBecauseTheApiMustShowWhatEachReleaseShowed()
    {
        await CommitAsync(1, null, Names("Old definition.", ["Rent caps"], []));
        var before = await ReadReleaseAsync(1);

        await CommitAsync(2, 1, Names("New definition.", ["Rent caps", "Eviction rules"], ["Zoning votes"], officialName: "Mayor Renamed"));

        Assert.Equal(before, await ReadReleaseAsync(1));
        Assert.Contains("2|mayor|Mayor Renamed", (await ReadReleaseAsync(2)).Single(row => row.StartsWith("official ", StringComparison.Ordinal)));
        Assert.Equal(["1 Old definition.", "2 New definition."], await ReadAsync("SELECT release_number || ' ' || definition FROM release_issues ORDER BY release_number"));
        Assert.Equal(["1:0:0", "2:0:0"], await ReadAsync("SELECT release_number || ':' || substantive_votes || ':' || bills FROM release_issues ORDER BY release_number"));
    }

    [Fact]
    public async Task ARefusedCommitLeavesNoNameRowBecauseTheRowsShareTheTransactionOfTheRelease()
    {
        var stored = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await publications.AddFirstPublishedAsync([new(PublishedRecordKind.Action, new string('a', 64), stored)], default);
        var refused = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            AddedEntries = [PublishedEntryFixtures.Action(new string('a', 64), stored.AddDays(3))],
            Names = Names("Definition.", [], [])
        };

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync("auth0|owner", refused, "key-refused", new string('0', 64), default));

        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_issues"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_officials"));
    }

    [Fact]
    public async Task ACountSnapshotFailureRollsBackTheReleaseAndItsNameRows()
    {
        await ExecuteAsync("""
            CREATE FUNCTION fail_home_issue_count_write() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                RAISE EXCEPTION 'forced issue count write failure';
            END;
            $function$;
            CREATE TRIGGER fail_home_issue_count_write BEFORE UPDATE OF count_congress ON release_issues
                FOR EACH ROW EXECUTE FUNCTION fail_home_issue_count_write();
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => CommitAsync(1, null, Names("Definition.", [], [])));

        Assert.Equal("P0001", failure.SqlState);
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM publication_releases"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_issues"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_officials"));
    }

    [Fact]
    public async Task APartyLineUpFailureRollsBackTheReleaseAndItsNameRowsBecauseTheLineUpIsWrittenInTheCommitTransaction()
    {
        await ExecuteAsync("""
            CREATE FUNCTION fail_party_lineup_write() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                RAISE EXCEPTION 'forced party line-up write failure';
            END;
            $function$;
            CREATE TRIGGER fail_party_lineup_write BEFORE INSERT ON release_issue_party_lineups
                FOR EACH ROW EXECUTE FUNCTION fail_party_lineup_write();
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => CommitAsync(1, null, Names("Definition.", [], [])));

        Assert.Equal("P0001", failure.SqlState);
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM publication_releases"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_issues"));
    }

    private async Task<string[]> ReadReleaseAsync(int release) => await ReadAsync($"""
        SELECT 'official ' || release_number || '|' || official_id || '|' || name FROM release_officials WHERE release_number = {release}
        UNION ALL SELECT 'term ' || release_number || '|' || "position" || '|' || office FROM release_official_terms WHERE release_number = {release}
        UNION ALL SELECT 'issue ' || release_number || '|' || issue_id || '|' || coalesce(definition, '') FROM release_issues WHERE release_number = {release}
        UNION ALL SELECT 'include ' || "position" || '|' || text FROM release_issue_includes WHERE release_number = {release}
        UNION ALL SELECT 'exclude ' || "position" || '|' || text FROM release_issue_excludes WHERE release_number = {release}
        UNION ALL SELECT 'area ' || policy_area_id || '|' || name FROM release_policy_areas WHERE release_number = {release}
        ORDER BY 1
        """);

    private static ReleaseNames Names(string definition, string[] includes, string[] excludes, string officialName = "Mayor Example")
    {
        PublishedOfficialProfile official = new()
        {
            Id = "mayor",
            Name = officialName,
            WikidataId = "Q1",
            Terms =
            [
                new() { Office = "Mayor", Kind = PublishedOfficeKind.Executive, Start = new(2024, 1, 1) },
                new() { Office = "Senator", Kind = PublishedOfficeKind.Senator, State = "WA", Party = "Democratic", Start = new(2020, 1, 3), End = new(2023, 1, 3) }
            ]
        };
        var issue = new ReleaseFacet(FacetKind.Issue, "housing", "Housing costs", [], null, new IssueDefinition(definition, [.. includes], [.. excludes]));
        var area = new ReleaseFacet(FacetKind.PolicyArea, "land-use", "Land use", [], null, null);
        return new ReleaseNames([official], new ReleaseFacets([issue], [area]));
    }

    private Task CommitAsync(int number, int? baseNumber, ReleaseNames names) =>
        publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            Names = names
        }, $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
