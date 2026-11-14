using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Ingest;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Ingest;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReleaseIngestTests(PostgresCollection postgres) : IAsyncLifetime
{
    private SearchWorld world = null!;

    public async Task InitializeAsync() => world = await SearchWorld.CreateAsync(postgres);

    public async Task DisposeAsync() => await world.DisposeAsync();

    [Fact]
    public async Task IngestingTheSameReleaseAgainChangesNothingSoARetryAfterACrashIsSafe()
    {
        await world.Site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap"), PublishedSamples.Statement('b', "Zoning"));
        var first = await world.IngestAsync();
        var again = await world.IngestAsync();

        Assert.Equal((IngestOutcome.Ingested, 2), (first.Outcome, first.Indexed));
        Assert.Equal((IngestOutcome.AlreadyActive, 0), (again.Outcome, again.Indexed));
        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM records"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM releases"));
    }

    [Fact]
    public async Task ARecordRevisionThatIsAlreadyStoredIsNotReadAgainSoACarriedRecordCostsNoRead()
    {
        var carried = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(1, carried);
        await world.Site.PublishAsync(2, carried, PublishedSamples.Statement('b', "Zoning"));
        await BreakTypedRowsAsync(carried);

        var result = await world.IngestAsync();

        Assert.Equal(1, result.Indexed);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task AReplacedRevisionClosesItsRangeAtTheLastReleaseThatHeldItAndTheNewRevisionOpensAtTheNewRelease()
    {
        await world.Site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap"));
        await world.IngestAsync();
        await world.Site.PublishAsync(2, PublishedSamples.Statement('a', "Rent cap revised", revision: 2));

        var result = await world.IngestAsync();

        Assert.Equal(1, result.Closed);
        Assert.Equal((1, 1), await RangeAsync(1));
        Assert.Equal((2, null), await RangeAsync(2));
    }

    [Fact]
    public async Task ADroppedRevisionClosesItsRangeAndAReturningRevisionGetsANewRangeWithoutARead()
    {
        var record = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(5, record);
        await world.PublishAndIngestAsync(6, PublishedSamples.Statement('b', "Zoning"));
        Assert.Equal((5, 5), await RangeAsync(1));
        await world.Site.PublishAsync(7, record, PublishedSamples.Statement('b', "Zoning"));
        await BreakTypedRowsAsync(record);

        var result = await world.IngestAsync();

        Assert.Equal(0, result.Indexed);
        Assert.Empty(result.Failures);
        Assert.Equal([(5, (int?)5), (7, null)], await RangesAsync(1));
    }

    [Fact]
    public async Task ARevisionThatIsInReleaseFiveGoneInSixAndBackInSevenIsInvisibleToASearchPinnedToSix()
    {
        var record = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(5, record);
        await world.PublishAndIngestAsync(6, PublishedSamples.Statement('b', "Zoning"));
        await world.PublishAndIngestAsync(7, record);

        Assert.Single((await world.SearchAsync(release: 5)).Hits, hit => hit.RecordId == record.Entry.RecordId);
        Assert.DoesNotContain((await world.SearchAsync(release: 6)).Hits, hit => hit.RecordId == record.Entry.RecordId);
        Assert.Single((await world.SearchAsync(release: 7)).Hits, hit => hit.RecordId == record.Entry.RecordId);
    }

    [Fact]
    public async Task AReturningRevisionKeepsTheBillLineOfItsStoredRow()
    {
        var action = PublishedSamples.Action("Doe sponsors a bill", "Doe sponsored it.", "A housing bill", "Sponsor: D000001");
        var withLine = action with { Entry = action.Entry with { BillLine = "This bill funds housing." } };
        var other = PublishedSamples.Statement('b', "Zoning");
        await world.PublishAndIngestAsync(5, withLine);
        await world.PublishAndIngestAsync(6, other);

        await world.PublishAndIngestAsync(7, withLine, other);

        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM records WHERE bill_line = 'This bill funds housing.'"));
    }

    [Fact]
    public async Task AReturningRevisionKeepsTheSummaryOfItsStoredRow()
    {
        var statement = PublishedSamples.Statement('a', "Rent cap", summary: "Tenants gain a rent cap.");
        var other = PublishedSamples.Statement('b', "Zoning");
        await world.PublishAndIngestAsync(5, statement);
        await world.PublishAndIngestAsync(6, other);

        await world.PublishAndIngestAsync(7, statement, other);

        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM records WHERE summary = 'Tenants gain a rent cap.'"));
    }

    [Fact]
    public async Task AWithdrawnRecordClosesItsRangeLikeADroppedOneAndAPinnedEarlierSearchStillFindsIt()
    {
        var withdrawn = PublishedSamples.Statement('a', "Rent cap");
        var kept = PublishedSamples.Statement('b', "Zoning");
        await world.PublishAndIngestAsync(5, withdrawn, kept);

        // Publication closes the range of a withdrawn revision, so release 6 lists only the kept record.
        var result = await world.PublishAndIngestAsync(6, kept);

        Assert.Equal(1, result.Closed);
        Assert.Equal((5, 5), await RangeAsync(1));
        Assert.Equal(["b"], (await world.SearchAsync()).Hits.Select(hit => hit.RecordId[..1]));
        Assert.Equal(["a", "b"], (await world.SearchAsync(release: 5)).Hits.Select(hit => hit.RecordId[..1]).Order());
    }

    [Fact]
    public async Task ARecordWithdrawnInSixAndReturnedInSevenInsertsTheNewRevisionWhileTheOldRangeStaysClosed()
    {
        var withdrawn = PublishedSamples.Statement('a', "Rent cap");
        var returned = PublishedSamples.Statement('a', "Rent cap fixed", revision: 2);
        await world.PublishAndIngestAsync(5, withdrawn);
        await world.PublishAndIngestAsync(6);

        await world.PublishAndIngestAsync(7, returned);

        Assert.Equal((5, 5), await RangeAsync(1));
        Assert.Equal((7, (int?)null), await RangeAsync(2));
        Assert.Empty((await world.SearchAsync(release: 6)).Hits);
        Assert.Equal(["Rent cap"], (await world.SearchAsync(release: 5)).Hits.Select(hit => hit.Headline));
        Assert.Equal(["Rent cap fixed"], (await world.SearchAsync()).Hits.Select(hit => hit.Headline));
    }

    [Fact]
    public async Task ActivatingAReleaseBeforeTheWithdrawalFindsTheWithdrawnRecordAgainWithoutARead()
    {
        var withdrawn = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(5, withdrawn);
        await world.PublishAndIngestAsync(6);
        await world.Site.ActivateAsync(5);
        await BreakTypedRowsAsync(withdrawn);

        var back = await world.IngestAsync();

        Assert.Equal(IngestOutcome.Activated, back.Outcome);
        Assert.Empty(back.Failures);
        Assert.Equal(["a"], (await world.SearchAsync()).Hits.Select(hit => hit.RecordId[..1]));
    }

    [Fact]
    public async Task AnActiveReleaseThatGoesBackToAStoredReleaseOnlyMovesTheActivePointerAndReadsNoRecord()
    {
        var first = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(5, first);
        await world.PublishAndIngestAsync(6, PublishedSamples.Statement('b', "Zoning"));
        await world.Site.ActivateAsync(5);
        await BreakTypedRowsAsync(first);

        var back = await world.IngestAsync();

        Assert.Equal(IngestOutcome.Activated, back.Outcome);
        Assert.Empty(back.Failures);
        Assert.Equal(5, (await world.Index.FindReleaseAsync(null, CancellationToken.None))!.ReleaseNumber);
        Assert.Equal(["a"], (await world.SearchAsync(release: 5)).Hits.Select(hit => hit.RecordId[..1]));
        Assert.Equal(["b"], (await world.SearchAsync(release: 6)).Hits.Select(hit => hit.RecordId[..1]));
    }

    [Fact]
    public async Task ARollbackThenANewerReleaseMakesTheNewerReleaseActive()
    {
        var first = PublishedSamples.Statement('a', "Rent cap");
        await world.PublishAndIngestAsync(5, first);
        await world.PublishAndIngestAsync(6, PublishedSamples.Statement('b', "Zoning"));
        await world.Site.ActivateAsync(5);
        await world.IngestAsync();

        var result = await world.PublishAndIngestAsync(7, first, PublishedSamples.Statement('c', "Transit"));

        Assert.Equal(IngestOutcome.Ingested, result.Outcome);
        Assert.Equal(7, (await world.Index.FindReleaseAsync(null, CancellationToken.None))!.ReleaseNumber);
        Assert.Equal(["a", "c"], (await world.SearchAsync()).Hits.Select(hit => hit.RecordId[..1]).Order());
    }

    [Fact]
    public async Task ARecordThatFailsItsContractValidationIsNotIndexedAndTheIngestReportsItWhileTheOthersAreIndexed()
    {
        var bad = PublishedSamples.Statement('a', "Rent cap");
        await world.Site.PublishAsync(1, bad, PublishedSamples.Statement('b', "Zoning"));
        // PublishedRecordEntry.Validate allows at most 64 issues. The table has no such limit.
        await ViewsSeed.ExecuteAsync(world.OperationalDatabase, $"""
            INSERT INTO published_entry_issues (kind, record_id, revision_number, "position", issue_id)
            SELECT 'statement', '{bad.Entry.RecordId}', 1, n, 'issue-' || n FROM generate_series(0, {PublishedRecordEntry.MaximumFilterValues}) n
            """);

        var result = await world.IngestAsync();

        Assert.Equal(1, result.Indexed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(bad.Entry.RecordId, failure.RecordId);
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM records WHERE record_id = '{bad.Entry.RecordId}'"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM records"));
    }

    [Fact]
    public async Task AReadFailureRollsBackTheWholeReleaseAndTheNextRunTriesAgain()
    {
        await world.Site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap"), PublishedSamples.Statement('b', "Zoning"));
        // The entry read throws when an official of a record has no name in the release. The index transaction is open by then.
        await ViewsSeed.ExecuteAsync(world.OperationalDatabase, "DELETE FROM release_officials WHERE release_number = 1");

        await Assert.ThrowsAsync<InvalidDataException>(world.IngestAsync);
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM releases"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM records"));

        await ViewsSeed.ExecuteAsync(world.OperationalDatabase, "INSERT INTO release_officials (release_number, official_id, name) VALUES (1, 'o1', 'Official o1')");
        var result = await world.IngestAsync();
        Assert.Equal(2, result.Indexed);
    }

    [Fact]
    public async Task AnOlderReleaseThatWasNeverStoredIsRefusedBecauseItsRangesCannotBeOrdered()
    {
        await world.PublishAndIngestAsync(5, PublishedSamples.Statement('a', "Rent cap"));
        await world.PublishAndIngestAsync(7, PublishedSamples.Statement('b', "Zoning"));
        await world.Site.ActivateAsync(6);

        await Assert.ThrowsAsync<InvalidDataException>(world.IngestAsync);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var command = world.Database.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // A read of the record would now fail, so a test that expects no read sees a failure if the ingest reads it.
    private Task BreakTypedRowsAsync(SampleRecord record) => ViewsSeed.ExecuteAsync(world.OperationalDatabase,
        $"SET session_replication_role = replica; DELETE FROM statement_candidates WHERE candidate_id = '{record.Entry.RecordId}'");

    private async Task<(int First, int? Last)> RangeAsync(int revision) => (await RangesAsync(revision))[0];

    private async Task<List<(int First, int? Last)>> RangesAsync(int revision)
    {
        await using var command = world.Database.CreateCommand(
            $"SELECT first_release, last_release FROM records WHERE record_id = '{new string('a', 64)}' AND revision_number = {revision} ORDER BY first_release");
        await using var reader = await command.ExecuteReaderAsync();
        var ranges = new List<(int, int?)>();
        while (await reader.ReadAsync()) ranges.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        return ranges;
    }
}
