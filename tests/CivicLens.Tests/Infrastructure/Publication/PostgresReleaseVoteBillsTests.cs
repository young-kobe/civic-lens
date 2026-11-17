using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseVoteBillsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Rows = "SELECT release_number || '|' || left(vote_record_id, 1) || '|' || left(bill_record_id, 1) FROM release_vote_bills ORDER BY 1";
    private static readonly string Vote1 = new('a', 64);
    private static readonly string Vote2 = new('b', 64);
    private static readonly string Action1 = new('c', 64);
    private static readonly string Action2 = new('d', 64);

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
    public async Task ACommitPinsTheVoteLinksOfItsReleaseAndALaterReleaseLeavesTheEarlierRowsAsTheyWereBecauseARollbackMustShowWhatEachReleaseShowed()
    {
        await CommitAsync(1, null, Directory(1), [new(Vote1, Action1)]);
        await CommitAsync(2, 1, Directory(2), [new(Vote1, Action2), new(Vote2, Action2)]);

        Assert.Equal(["1|a|c", "2|a|d", "2|b|d"], await ReadAsync(Rows));
    }

    [Fact]
    public async Task TheStoreWritesOnlyTheLinksThatAReleaseLacksAndNeverChangesALinkThatExists()
    {
        await CommitAsync(1, null, Directory(1), [new(Vote1, Action1)]);

        var written = await publications.AddVoteBillsAsync(1, [new(Vote1, Action2), new(Vote2, Action2)], default);

        Assert.Equal(1, written);
        Assert.Equal(["1|a|c", "1|b|d"], await ReadAsync(Rows));
    }

    [Fact]
    public async Task TheBackfillWritesTheLinksThatTheCommitWroteAndASecondRunChangesNothing()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        var unmatched = scenario.AddVote('e', billType: null);
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Action, action), new PublishedRecordReference(PublishedRecordKind.Vote, vote),
            new PublishedRecordReference(PublishedRecordKind.Vote, unmatched)));
        await CommitAsync(1, null, KeepFolder(scenario, summary), [.. scenario.Publications.LastCommit!.VoteBills]);
        var written = await ReadAsync(Rows);
        await ExecuteAsync("DELETE FROM release_vote_bills");
        var backfill = StoredColumnsBackfills.WithoutRosters(publications, scenario.Releases, scenario.Votes);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(["1|d|c"], written);
        Assert.Equal(written, await ReadAsync(Rows));
        Assert.Equal((1, 0), (first.VoteBillLinksFilled, second.VoteBillLinksFilled));
    }

    [Fact]
    public async Task TheBackfillSkipsAReleaseWhoseFolderIsMissingAndFillsTheOthers()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Action, action), new PublishedRecordReference(PublishedRecordKind.Vote, vote)));
        await CommitAsync(1, null, KeepFolder(scenario, summary), []);
        await CommitAsync(2, 1, Directory(2), []);

        var result = await StoredColumnsBackfills.WithoutRosters(publications, scenario.Releases, scenario.Votes).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, result.VoteBillLinksFilled);
        Assert.Equal(["1|d|c"], await ReadAsync(Rows));
    }

    [Fact]
    public async Task TheBackfillCountsAReleaseWhoseManifestCannotBeReadAndTheCommandFailsBecauseThatReleaseKeepsNoLink()
    {
        var (scenario, _) = await PublishedReleaseAsync();
        var broken = Directory(2);
        scenario.Releases.Directories[broken] = new() { [PublicationProtocol.ManifestPath] = "{"u8.ToArray() };
        await CommitAsync(2, 1, broken, []);
        var backfill = StoredColumnsBackfills.WithoutRosters(publications, scenario.Releases, scenario.Votes);

        var result = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "columns", "backfill"], null!, null!, null!, null!, null!, null!, backfill, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

        Assert.Equal((0, 1), (result.VoteBillLinksFilled, result.ReleasesWithUnreadableManifest));
        Assert.Equal(1, exit);
        Assert.Equal(["1|d|c"], await ReadAsync(Rows));
    }

    [Fact]
    public async Task TheVerifierPassesStoredLinksThatEqualTheRuleSkipsAReleaseWithNoFolderAndWritesNothing()
    {
        var (scenario, _) = await PublishedReleaseAsync();
        await CommitAsync(2, 1, Directory(2), []);
        var before = await ReadAsync(Rows);

        var result = await VerifyAsync(scenario);

        Assert.Equal((1, 0, 1), (result.Passed, result.Failed, result.Retired));
        Assert.Equal(before, await ReadAsync(Rows));
        Assert.Equal(0, await ExitOfVerifyAsync(scenario));
    }

    [Theory]
    [InlineData("DELETE FROM release_vote_bills", "linkMissing")]
    [InlineData("INSERT INTO release_vote_bills VALUES (1, '{e}', '{c}')", "linkExtra")]
    [InlineData("UPDATE release_vote_bills SET bill_record_id = '{e}'", "linkChanged")]
    public async Task TheVerifierFailsAReleaseWhoseStoredLinksDifferFromTheRuleAndNamesTheVoteBecauseTheViewReadsOnlyTheStoredRows(string change, string reason)
    {
        var (scenario, vote) = await PublishedReleaseAsync();
        await ExecuteAsync(change.Replace("{e}", new string('e', 64), StringComparison.Ordinal).Replace("{c}", new string('c', 64), StringComparison.Ordinal));
        var after = await ReadAsync(Rows);

        var result = await VerifyAsync(scenario);

        var failure = Assert.Single(result.FailedReleases);
        Assert.Equal((1, 1, reason), (result.Failed, failure.ReleaseNumber, failure.Reason));
        Assert.Equal(reason == "linkExtra" ? $"vote:{new string('e', 64)}" : $"vote:{vote}", failure.Record);
        Assert.Equal(after, await ReadAsync(Rows));
        Assert.Equal(1, await ExitOfVerifyAsync(scenario));
    }

    [Fact]
    public async Task TheVerifierFailsAReleaseWhoseManifestCannotBeRead()
    {
        var (scenario, _) = await PublishedReleaseAsync();
        var broken = Directory(2);
        scenario.Releases.Directories[broken] = new() { [PublicationProtocol.ManifestPath] = "{"u8.ToArray() };
        await CommitAsync(2, 1, broken, []);

        var result = await VerifyAsync(scenario);

        Assert.Equal((1, 1, "manifestInvalid"), (result.Passed, result.Failed, Assert.Single(result.FailedByReason).Key));
    }

    // The order and the lateral shape of VoteStore.ListSql, with no filter, for the first page of three rows.
    private const string ListQuery = """
        SELECT a.release_number, v.record_id, v.bill_record_id, v.yea
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.votes v
                WHERE TRUE
                ORDER BY v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id LIMIT 3) v
            ON TRUE
         ORDER BY v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id
        """;

    [Fact]
    public async Task TheFirstPageOfTheVoteListReadsOnePinnedRowForEachVoteAndNoAggregateOfTheBillView()
    {
        const int bills = 5000;
        await SeedManyBillsAsync(bills);
        await ExecuteAsync($"""
            INSERT INTO release_vote_bills (release_number, vote_record_id, bill_record_id)
            SELECT 1, r.record_id, md5(r.bill_number::text) || md5('x' || r.bill_number::text) FROM vote_revisions r;
            ANALYZE release_vote_bills;
            """);

        var plan = await ExplainAsync();

        Assert.DoesNotContain("Aggregate", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on action_revisions", plan, StringComparison.Ordinal);
        Assert.Contains("release_vote_bills", plan, StringComparison.Ordinal);
    }

    // Vote n links bill n. The bills carry the record ids of md5(n) and md5('x' + n), as the seed of the issue fill test does.
    private async Task SeedManyBillsAsync(int bills)
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(connectionString, [.. Enumerable.Range(0, 16).Select(index => new SeededVote("0123456789abcdef"[index], $"2026-03-{index + 1:00}") { BillNumber = index + 1 })]);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var statement in $"""
            SET session_replication_role = replica;
            {PublicActionRows.ActionRevision("template", 1, "Person", "2025-02-01", 1, introducedOn: null)}
            CREATE TEMP TABLE bulk_ids AS
            SELECT n, md5(n::text) || md5('x' || n::text) AS id FROM generate_series(1, {bills}) n;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            SELECT 'action', id, 1, 1, NULL FROM bulk_ids;
            INSERT INTO action_revisions
            SELECT x.*
              FROM action_revisions r, bulk_ids b,
                   LATERAL jsonb_populate_record(NULL::action_revisions,
                       to_jsonb(r) || jsonb_build_object('record_id', b.id, 'bill_number', b.n, 'run_id', NULL, 'headline', 'Headline ' || b.n)) x
             WHERE r.record_id = 'template';
            DELETE FROM action_revisions WHERE record_id = 'template';
            INSERT INTO action_revision_sponsorships (record_id, revision_number, "position", person_id, bioguide_id, role, is_original, sponsored_on)
            SELECT id, 1, 0, 'p1', 'Bp1', 'Sponsor', true, DATE '2025-02-01' FROM bulk_ids;
            ANALYZE publication_record_states, action_revisions, action_revision_sponsorships, vote_revisions;
            """.Split(";\n", StringSplitOptions.RemoveEmptyEntries))
        {
            await using var command = new NpgsqlCommand(statement, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<string> ExplainAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, COSTS OFF, TIMING OFF) {ListQuery}", connection);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private async Task<(PublicationScenario Scenario, string Vote)> PublishedReleaseAsync()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        var vote = scenario.AddVote('d', billActionRecordIds: [action]);
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Action, action), new PublishedRecordReference(PublishedRecordKind.Vote, vote)));
        await CommitAsync(1, null, KeepFolder(scenario, summary), [.. scenario.Publications.LastCommit!.VoteBills]);
        return (scenario, vote);
    }

    private Task<ReleaseVoteBillsVerification> VerifyAsync(PublicationScenario scenario) =>
        new VerifyReleaseVoteBills(publications, scenario.Releases, scenario.Votes).ExecuteAsync(PublicationScenario.Owner);

    private Task<int> ExitOfVerifyAsync(PublicationScenario scenario) => ReleaseCommand.ExecuteAsync(["releases", "vote-bills", "verify"], null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        new VerifyReleaseVoteBills(publications, scenario.Releases, scenario.Votes), null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

    private Task CommitAsync(int number, int? baseNumber, string directory, ReleaseVoteBill[] voteBills) =>
        publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, directory, [], [])
        {
            Withdrawals = [],
            Names = ReleaseNameFixtures.None,
            VoteBills = [.. voteBills]
        }, $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    // The store accepts only a folder name of its own form, so the folder of the scenario release moves to such a name.
    private static string KeepFolder(PublicationScenario scenario, PublicationReleaseSummary release)
    {
        var directory = Directory(1);
        scenario.Releases.Directories[directory] = scenario.Releases.Directories[release.DirectoryName];
        return directory;
    }

    private static string Directory(int number) => $"{number:D6}-{Guid.NewGuid():N}";

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

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
}
