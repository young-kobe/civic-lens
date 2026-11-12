using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
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
        var backfill = new BackfillStoredColumns(publications, scenario.Releases, scenario.Votes);

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

        var result = await new BackfillStoredColumns(publications, scenario.Releases, scenario.Votes).ExecuteAsync(PublicationScenario.Owner);

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
        var backfill = new BackfillStoredColumns(publications, scenario.Releases, scenario.Votes);

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
