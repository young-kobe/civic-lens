using CivicLens.PublicApi.Officials;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Officials;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialActivityReaderTests(PostgresCollection postgres)
{
    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    [Fact]
    public async Task StoredSnapshotGivesTheSameBillFiguresAsTheLiveViewsAndReadsOneBoundedQueryForBills()
    {
        var database = await SeedAsync();
        var selections = new[] { ("p0", 118), ("p1", 119), ("p2", 119), ("p3", 119) };
        var live = await ReadAsync(database, selections);
        await StoreSnapshotAsync(database, 118, 119);

        var stored = await ReadAsync(database, selections);

        Assert.Equal(Figures(live.Figures), Figures(stored.Figures));
        Assert.Equal(["118 p0 2 0 2", "119 p1 1 1 3", "119 p2 0 1 3", "119 p3 0 0 3"], Figures(stored.Figures));
        Assert.Single(stored.Commands, text => text.Contains("release_bill_counts", StringComparison.Ordinal));
        Assert.DoesNotContain(stored.Commands, text => text.Contains("public_api.bills", StringComparison.Ordinal));
        Assert.Equal(2, stored.Commands.Count);
    }

    [Fact]
    public async Task TheReaderShowsTheStoredRowsAndNotARecount()
    {
        var database = await SeedAsync();
        await StoreSnapshotAsync(database, 118, 119);
        await ViewsSeed.ExecuteAsync(database, """
            UPDATE release_bill_counts SET bills = 40 WHERE congress = 119;
            UPDATE release_official_bill_counts SET sponsored = 9 WHERE official_id = 'p1';
            """);

        var read = await ReadAsync(database, [("p1", 119)]);

        Assert.Equal(["119 p1 9 1 40"], Figures(read.Figures));
    }

    [Fact]
    public async Task AMissingMemberRowUnderAStoredCongressTotalMeansZeroAndRunsNoRecount()
    {
        var database = await SeedAsync();
        await StoreSnapshotAsync(database, 118, 119);

        var read = await ReadAsync(database, [("p3", 119)]);

        Assert.Equal(["119 p3 0 0 3"], Figures(read.Figures));
        Assert.DoesNotContain(read.Commands, text => text.Contains("public_api.bills", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACongressWithoutAStoredTotalIsReadLiveForThatCongressOnlyAndIsNeverZero()
    {
        var database = await SeedAsync();
        await StoreSnapshotAsync(database, 118);
        await ViewsSeed.ExecuteAsync(database, "UPDATE release_bill_counts SET bills = 30 WHERE congress = 118;");

        var read = await ReadAsync(database, [("p0", 118), ("p1", 119)]);

        Assert.Equal(["118 p0 2 0 30", "119 p1 1 1 3"], Figures(read.Figures));
        Assert.Single(read.Commands, text => text.Contains("public_api.bills", StringComparison.Ordinal));
    }

    private async Task<string> SeedAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'");
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(database,
            [.. new[] { "p0", "p1", "p2", "p3" }.Select(id => new SeededOfficial(id, $"Name {id}", new SeededTerm("Senator", "Senator", "2019-01-03") { State = "MA" }))]);
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('a', "2024-05-02") { Congress = 118, BillNumber = 1, Sponsorships = [new("p0", "Sponsor", "2024-05-02")] },
            new SeededBill('b', "2024-05-03") { Congress = 118, BillNumber = 2, Sponsorships = [new("p0", "Sponsor", "2024-05-03"), new("p1", "Cosponsor", "2024-05-03")] },
            new SeededBill('c', "2026-02-02") { Congress = 119, BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-02-02"), new("p1", "Cosponsor", "2026-02-02"), new("p2", "Cosponsor", "2026-02-02", "2026-03-01")] },
            new SeededBill('d', "2026-02-03") { Congress = 119, BillNumber = 4, Sponsorships = [new("p1", "Cosponsor", "2026-02-03"), new("p2", "Cosponsor", "2026-02-03")] },
            new SeededBill('e', "2026-02-04") { Congress = 119, BillNumber = 5, Sponsorships = [new("p0", "Sponsor", "2026-02-04")] });
        return database;
    }

    /// <summary>Stores the snapshot of release 1 from the live views, as a commit stores it: one total for each Congress and sparse nonzero member rows.</summary>
    private static Task StoreSnapshotAsync(string database, params int[] congresses)
    {
        var list = string.Join(", ", congresses);
        return ViewsSeed.ExecuteAsync(database, $"""
            INSERT INTO release_bill_counts (release_number, congress, bills, expected_member_row_count)
            SELECT 1, b.congress, count(*), (SELECT count(*) FROM public_api.official_bill_counts m
                                              WHERE m.congress = b.congress AND m.sponsored + m.cosponsored > 0 AND m.official_id IN (SELECT official_id FROM release_officials))
              FROM public_api.bills b WHERE b.congress IN ({list}) GROUP BY b.congress;
            INSERT INTO release_official_bill_counts (release_number, congress, official_id, sponsored, cosponsored)
            SELECT 1, m.congress, m.official_id, m.sponsored, m.cosponsored FROM public_api.official_bill_counts m
             WHERE m.congress IN ({list}) AND m.sponsored + m.cosponsored > 0 AND m.official_id IN (SELECT official_id FROM release_officials);
            """);
    }

    private static string[] Figures(Dictionary<(string OfficialId, int Congress), OfficialActivityFigures> figures) =>
        [.. figures.OrderBy(pair => pair.Key.Congress).ThenBy(pair => pair.Key.OfficialId, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Congress} {pair.Key.OfficialId} {pair.Value.Bills.Sponsored} {pair.Value.Bills.Cosponsored} {pair.Value.Bills.BillsInCongress}")];

    private static async Task<ReaderRun> ReadAsync(string database, (string OfficialId, int Congress)[] selections)
    {
        var commands = new CommandLog();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(commands).SetMinimumLevel(LogLevel.Trace));
        var builder = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(database) { Username = PublicRole, Password = PublicPassword }.ConnectionString);
        builder.UseLoggerFactory(loggerFactory);
        await using var views = builder.Build();
        await using var connection = await views.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var figures = await OfficialActivityReader.ReadAsync(connection, transaction, selections, default);
        return new(figures, [.. commands.Texts.Where(text => text.Contains("public_api.", StringComparison.Ordinal))]);
    }

    private sealed record ReaderRun(Dictionary<(string OfficialId, int Congress), OfficialActivityFigures> Figures, IReadOnlyList<string> Commands);

    private sealed class CommandLog : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> texts = new();

        public IReadOnlyCollection<string> Texts => [.. texts];
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, texts);
        public void Dispose()
        {
        }

        private sealed class Capture(string category, System.Collections.Concurrent.ConcurrentQueue<string> texts) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category != "Npgsql.Command" || eventId.Name != "CommandExecutionCompleted" || state is not IReadOnlyList<KeyValuePair<string, object?>> values) return;
                var text = values.FirstOrDefault(item => string.Equals(item.Key, "CommandText", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                if (text is not null) texts.Enqueue(text);
            }
        }
    }
}
