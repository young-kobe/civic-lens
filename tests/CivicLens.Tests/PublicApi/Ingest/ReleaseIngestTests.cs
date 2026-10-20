using CivicLens.PublicApi.Ingest;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

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
        world.Site.Publish(1, PublishedSamples.Statement('a', "Rent cap"), PublishedSamples.Statement('b', "Zoning"));
        var first = await world.IngestAsync();
        var again = await new ReleaseIngest(new ReleaseFeed(new HttpClient(world.Site) { BaseAddress = world.Options.ReleaseBaseUrl }), world.Database)
            .RunAsync(CancellationToken.None);

        Assert.Equal((IngestOutcome.Ingested, 2), (first.Outcome, first.Indexed));
        Assert.Equal((IngestOutcome.NotNewer, 0), (again.Outcome, again.Indexed));
        Assert.Equal(2L, await CountAsync("SELECT count(*) FROM records"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM releases"));
    }

    [Fact]
    public async Task AnUnchangedManifestAnswers304AndTheIngestFetchesNoRecord()
    {
        world.Site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        await world.IngestAsync();
        world.Site.RecordRequests.Clear();

        var second = await world.IngestAsync();

        Assert.Equal(IngestOutcome.NotModified, second.Outcome);
        Assert.Equal(1, world.Site.NotModifiedReplies);
        Assert.Empty(world.Site.RecordRequests);
    }

    [Fact]
    public async Task ARecordRevisionThatIsAlreadyStoredIsNotFetchedAgainSoACarriedRecordCostsNoRequest()
    {
        var carried = PublishedSamples.Statement('a', "Rent cap");
        world.Site.Publish(1, carried);
        await world.IngestAsync();
        world.Site.RecordRequests.Clear();

        world.Site.Publish(2, carried, PublishedSamples.Statement('b', "Zoning"));
        var result = await world.IngestAsync();

        Assert.Equal(1, result.Indexed);
        Assert.Equal([$"records/{new string('b', 64)}.json"], world.Site.RecordRequests);
    }

    [Fact]
    public async Task AReplacedRevisionClosesItsRangeAtTheLastReleaseThatHeldItAndTheNewRevisionOpensAtTheNewRelease()
    {
        world.Site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        await world.IngestAsync();
        world.Site.Publish(2, PublishedSamples.Statement('a', "Rent cap revised", revision: 2));

        var result = await world.IngestAsync();

        Assert.Equal(1, result.Closed);
        Assert.Equal((1, 1), await RangeAsync(1));
        Assert.Equal((2, null), await RangeAsync(2));
    }

    [Fact]
    public async Task ADroppedRevisionClosesItsRangeAndAReturningRevisionIsOpenAgain()
    {
        var record = PublishedSamples.Statement('a', "Rent cap");
        world.Site.Publish(1, record);
        await world.IngestAsync();
        world.Site.Publish(2, PublishedSamples.Statement('b', "Zoning"));
        await world.IngestAsync();
        Assert.Equal((1, 1), await RangeAsync(1));

        world.Site.Publish(3, record, PublishedSamples.Statement('b', "Zoning"));
        await world.IngestAsync();

        Assert.Equal((1, null), await RangeAsync(1));
    }

    [Fact]
    public async Task ARecordThatFailsItsContractValidationIsNotIndexedAndTheIngestReportsItWhileTheOthersAreIndexed()
    {
        var bad = PublishedSamples.Statement('a', "Rent cap");
        world.Site.Publish(1, bad, PublishedSamples.Statement('b', "Zoning"));
        world.Site.Corrupt(bad.Entry.RecordId, json => json.Replace("Xq the source text.", "Changed text.", StringComparison.Ordinal));

        var result = await world.IngestAsync();

        Assert.Equal(1, result.Indexed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(bad.Entry.RecordId, failure.RecordId);
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM records WHERE record_id = '{bad.Entry.RecordId}'"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM records"));
    }

    [Fact]
    public async Task ARecordFileThatDoesNotMatchItsManifestEntryIsNotIndexed()
    {
        var first = PublishedSamples.Statement('a', "Rent cap");
        var second = PublishedSamples.Statement('b', "Zoning");
        world.Site.Publish(1, first, second);
        world.Site.Corrupt(second.Entry.RecordId, _ => System.Text.Json.JsonSerializer.Serialize(first.Body, CivicLens.Publication.Contracts.PublicationProtocol.JsonOptions));

        var result = await world.IngestAsync();

        Assert.Equal(second.Entry.RecordId, Assert.Single(result.Failures).RecordId);
    }

    [Fact]
    public async Task AFetchFailureRollsBackTheWholeReleaseAndTheNextRunTriesAgain()
    {
        var missing = PublishedSamples.Statement('b', "Zoning");
        world.Site.Publish(1, PublishedSamples.Statement('a', "Rent cap"), missing);
        world.Site.Remove(missing.Entry.RecordId);

        await Assert.ThrowsAsync<HttpRequestException>(world.IngestAsync);
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM releases"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM records"));

        world.Site.Restore(missing.Entry.RecordId);
        var result = await world.IngestAsync();
        Assert.Equal(2, result.Indexed);
    }

    [Fact]
    public async Task AnOlderReleaseNumberIsNotIngestedSoARollbackOfTheStaticSiteDoesNotMoveTheActiveRelease()
    {
        world.Site.Publish(2, PublishedSamples.Statement('a', "Rent cap"));
        await world.IngestAsync();
        world.Site.Publish(1, PublishedSamples.Statement('b', "Zoning"));

        var result = await world.IngestAsync();

        Assert.Equal(IngestOutcome.NotNewer, result.Outcome);
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM records"));
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var command = world.Database.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(int First, int? Last)> RangeAsync(int revision)
    {
        await using var command = world.Database.CreateCommand(
            $"SELECT first_release, last_release FROM records WHERE record_id = '{new string('a', 64)}' AND revision_number = {revision}");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }
}
