using CivicLens.PublicApi.ZipDistricts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ZipMemberUpdatesStoreTests(PostgresCollection postgres)
{
    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    [Fact]
    public async Task CountsDistinctCurrentMemberVotesFirstPublishedAfterMarkerAcrossRollCallKinds()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SetUpDistrictAsync(database, 4);
            var votes = Enumerable.Range(0, 21).Select(index => new SeededVote('a', "2026-05-01")
            {
                RecordId = (100 + index).ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
                RollCall = index + 1,
                FirstRelease = index % 2 == 0 ? 2 : 3,
                ActiveRevision = index == 0 ? 2 : 1,
                Kind = (index % 3) switch { 0 => "Passage", 1 => "Amendment", _ => "Rule" },
                Casts = ["Yea", "Nay"],
                PersonIds = ["p0", "p1"]
            }).ToList();
            votes.Add(new SeededVote('0', "2026-05-02") { FirstRelease = 3, ActiveRevision = 2, Casts = ["Yea"], PersonIds = ["p0"] });
            votes.Add(new SeededVote('b', "2026-05-03") { FirstRelease = 4, Casts = ["Yea"], PersonIds = ["p0"] });
            votes.Add(new SeededVote('c', "2026-05-04") { FirstRelease = 5, Casts = ["Yea"], PersonIds = ["p0"] });
            votes.Add(new SeededVote('d', "2026-05-05") { FirstRelease = 2, LastRelease = 3, Casts = ["Yea"], PersonIds = ["p0"] });
            votes.Add(new SeededVote('e', "2026-05-06") { FirstRelease = 2, Casts = ["Yea"], PersonIds = ["outside"] });
            votes.Add(new SeededVote('f', "2026-05-07") { FirstRelease = 3, Casts = ["Yea"], PersonIds = ["p0"] });
            await ViewsSeed.AddVotesAsync(database, [.. votes]);
            await ViewsSeed.ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
                VALUES ('vote', '{new string('0', 64)}', 1, 1, 2), ('vote', '{new string('f', 64)}', 1, 1, 1);
                """);

            await using var views = OpenPublicViews(database);
            var store = new ZipDistrictStore(views);
            var lookup = await store.FindMembersAsync(new ZipDistrictRequest { Zcta = "36009" }, null, 1, default);

            Assert.Equal(4, lookup.Answer!.Release);
            Assert.Equal(22, lookup.NewVoteCount);
            Assert.NotNull(lookup.SharedVotes);
            Assert.NotNull(lookup.Answer.Districts[0].Representative!.MissedVotes);
        }
        finally
        {
            await postgres.DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task StripLoadsOnlyCurrentHoldersAndReturnsNullOrZeroForUncountedMarkers()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SetUpDistrictAsync(database, 4);
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2026-05-01") { FirstRelease = 2, Casts = ["Yea"], PersonIds = ["p0"] });
            await ViewsSeed.ExecuteAsync(database, "REVOKE SELECT ON public_api.officials, public_api.official_attendance, public_api.official_party_lines, public_api.official_bill_counts, public_api.official_issue_counts, public_api.member_votes, public_api.release_issues, public_api.bills, public_api.vote_issues FROM civic_lens_public");

            await using var views = OpenPublicViews(database);
            var store = new ZipDistrictStore(views);
            var request = new ZipDistrictRequest { Zcta = "36009" };
            var withMarker = await store.FindStripAsync(request, 1, default);
            await ViewsSeed.ExecuteAsync(database, "REVOKE SELECT ON public_api.vote_first_publications FROM civic_lens_public");
            var withoutMarker = await store.FindStripAsync(request, null, default);
            var equalMarker = await store.FindStripAsync(request, 4, default);
            var futureMarker = await store.FindStripAsync(request, 5, default);
            var noHoldersRequest = new ZipDistrictRequest { Zcta = "99501" };
            var noHolders = await store.FindStripAsync(noHoldersRequest, 1, default);

            Assert.Null(withoutMarker.NewVoteCount);
            Assert.Equal(1, withMarker.NewVoteCount);
            Assert.Equal(0, equalMarker.NewVoteCount);
            Assert.Equal(0, futureMarker.NewVoteCount);
            Assert.Equal(0, noHolders.NewVoteCount);
            Assert.Equal("Pat House", withoutMarker.Answer!.Districts[0].Representative!.Name);
            Assert.Empty(withoutMarker.IssueOptions);
            Assert.Null(withoutMarker.SharedVotes);
        }
        finally
        {
            await postgres.DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task StripMembersAndNewVoteCountUseTheReleaseReadBeforeActivationChanges()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        using var gate = new FirstPublicApiReadGate(TimeSpan.FromSeconds(15));
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(gate).SetMinimumLevel(LogLevel.Trace));
        Task<ZipLookup>? read = null;
        try
        {
            await SetUpDistrictAsync(database, 4);
            await ZipSeed.AddNamesAsync(database, 5,
                new SeededSeat("p0", "Pat Next Release", "Representative", "AL", 2),
                new SeededSeat("p1", "Sam Next Release", "Senator", "AL", null));
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2026-05-01") { FirstRelease = 3, LastRelease = 4, Casts = ["Yea"], PersonIds = ["p0"] },
                new SeededVote('b', "2026-05-02") { FirstRelease = 5, Casts = ["Yea"], PersonIds = ["p0"] },
                new SeededVote('c', "2026-05-03") { FirstRelease = 5, Casts = ["Yea"], PersonIds = ["p0"] });
            await using var views = OpenPublicViews(database, loggerFactory);
            var store = new ZipDistrictStore(views);
            var request = new ZipDistrictRequest { Zcta = "36009" };
            read = Task.Run(() => store.FindStripAsync(request, 1, default));
            await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(15));
            await ViewsSeed.ActivateReleaseAsync(database, 5, new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));
            gate.Resume();

            var first = await read.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(4, first.Answer!.Release);
            Assert.Equal("Pat House", first.Answer.Districts[0].Representative!.Name);
            Assert.Equal(1, first.NewVoteCount);
            var second = await store.FindStripAsync(request, 1, default);
            Assert.Equal(5, second.Answer!.Release);
            Assert.Equal("Pat Next Release", second.Answer.Districts[0].Representative!.Name);
            Assert.Equal(2, second.NewVoteCount);
        }
        finally
        {
            gate.Resume();
            if (read is not null) { try { await read.WaitAsync(TimeSpan.FromSeconds(15)); } catch { } }
            await postgres.DropDatabaseAsync(database);
        }
    }

    private static async Task SetUpDistrictAsync(string database, int release)
    {
        var published = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        for (var number = 1; number <= release + 1; number++)
            await ViewsSeed.ActivateReleaseAsync(database, number, published.AddDays(number - 1));
        await ViewsSeed.ActivateReleaseAsync(database, release, published.AddDays(release - 1));
        await ZipSeed.AdmitDistrictsAsync(database);
        await ZipSeed.AddNamesAsync(database, release,
            new SeededSeat("p0", "Pat House", "Representative", "AL", 2),
            new SeededSeat("p1", "Sam Senate", "Senator", "AL", null));
        await using var owner = new NpgsqlConnection(database);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand($"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'", owner);
        await command.ExecuteNonQueryAsync();
    }

    private static NpgsqlDataSource OpenPublicViews(string database, ILoggerFactory? loggerFactory = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(database) { Username = PublicRole, Password = PublicPassword }.ConnectionString;
        var builder = new NpgsqlDataSourceBuilder(connection);
        if (loggerFactory is not null) builder.UseLoggerFactory(loggerFactory);
        return builder.Build();
    }
}
