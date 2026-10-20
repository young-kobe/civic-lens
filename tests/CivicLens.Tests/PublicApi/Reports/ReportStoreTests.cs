using CivicLens.PublicApi.Reports;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Reports;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReportStoreTests(PostgresCollection postgres)
{
    private static readonly string RecordId = new('a', 64);

    private static async Task<SearchWorld> WorldWithReportsAsync(PostgresCollection postgres, int count)
    {
        var world = await SearchWorld.CreateAsync(postgres);
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap"));
        for (var i = 0; i < count; i++)
            Assert.Equal(ReportReceipt.Stored, await world.Reports.StoreAsync(new(RecordId, 1, "other", $"report {i}", "token"), CancellationToken.None));
        return world;
    }

    private static async Task<Guid> EpochAsync(SearchWorld world)
    {
        var batch = await world.Reports.PullAsync(null, 1, CancellationToken.None);
        return ReportCursor.Parse(batch.NextCursor)!.Epoch;
    }

    private static async Task<long> AckedAsync(SearchWorld world)
    {
        await using var command = world.Database.CreateCommand("SELECT acked_sequence FROM report_pull_state");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task PagesComeInSequenceOrderAndEachReportComesOnceSoThePullerMissesNothing()
    {
        await using var world = await WorldWithReportsAsync(postgres, 5);

        var texts = new List<string>();
        ReportCursor? cursor = null;
        for (var page = 0; page < 3; page++)
        {
            var batch = await world.Reports.PullAsync(cursor, 2, CancellationToken.None);
            texts.AddRange(batch.Reports.Select(report => report.Text));
            cursor = ReportCursor.Parse(batch.NextCursor);
        }

        Assert.Equal(["report 0", "report 1", "report 2", "report 3", "report 4"], texts);
        Assert.Empty((await world.Reports.PullAsync(cursor, 2, CancellationToken.None)).Reports);
    }

    [Fact]
    public async Task AnEmptyPullKeepsTheCursorSoThePullerCanPollWithoutLosingItsPlace()
    {
        await using var world = await WorldWithReportsAsync(postgres, 2);
        var last = ReportCursor.Parse((await world.Reports.PullAsync(null, 10, CancellationToken.None)).NextCursor);

        var batch = await world.Reports.PullAsync(last, 10, CancellationToken.None);

        Assert.Empty(batch.Reports);
        Assert.Equal(last!.Encode(), batch.NextCursor);
    }

    [Fact]
    public async Task ACursorOfAnotherEpochStartsFromTheFirstReportAndAcknowledgesNothingSoAResetNeverSkipsReports()
    {
        await using var world = await WorldWithReportsAsync(postgres, 3);

        var batch = await world.Reports.PullAsync(new ReportCursor(Guid.NewGuid(), 3), 10, CancellationToken.None);

        Assert.Equal(3, batch.Reports.Length);
        Assert.Equal(0, await AckedAsync(world));
    }

    [Fact]
    public async Task TheAcknowledgementMovesForwardOnlySoAnOldCursorCannotMakeAPulledReportUnpurgeable()
    {
        await using var world = await WorldWithReportsAsync(postgres, 4);
        var epoch = await EpochAsync(world);

        await world.Reports.PullAsync(new ReportCursor(epoch, 3), 10, CancellationToken.None);
        await world.Reports.PullAsync(new ReportCursor(epoch, 1), 10, CancellationToken.None);

        Assert.Equal(3, await AckedAsync(world));
    }

    [Fact]
    public async Task ThePurgeDeletesOnlyReportsThatAreAcknowledgedAndOlderThanSevenDays()
    {
        await using var world = await WorldWithReportsAsync(postgres, 4);
        var epoch = await EpochAsync(world);
        await world.Reports.PullAsync(new ReportCursor(epoch, 2), 10, CancellationToken.None);
        await using (var age = world.Database.CreateCommand("UPDATE reader_reports SET received_at = now() - interval '8 days' WHERE body IN ('report 0', 'report 2', 'report 3')"))
            await age.ExecuteNonQueryAsync();

        var purged = await world.Reports.PurgeAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        var remaining = (await world.Reports.PullAsync(new ReportCursor(Guid.NewGuid(), 0), 10, CancellationToken.None)).Reports.Select(report => report.Text);
        Assert.Equal(["report 1", "report 2", "report 3"], remaining);
    }
}
