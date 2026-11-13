using System.Data;
using CivicLens.PublicApi.Records;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Records;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class CorrectionHistoryStoreTests(PostgresCollection postgres)
{
    private static readonly string Id = new('a', 64);

    private static readonly PublishedCorrection[] History =
    [
        new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero), Note = "Newest note." },
        new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new DateTimeOffset(2026, 5, 1, 12, 30, 0, TimeSpan.Zero) }
    ];

    private async Task<(ApiHost Host, NpgsqlDataSource Views)> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, DateTimeOffset.UnixEpoch);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('vote', '{Id}', 1, 3, NULL);
            """);
        await CorrectionHistorySeed.BindAsync(host.OperationalDatabase, 1, PublishedRecordKind.Vote, Id, History);
        return (host, NpgsqlDataSource.Create(host.Options.PublicViewsDatabase));
    }

    [Fact]
    public async Task APageReadInsideItsTransactionGetsTheHistoryOfTheShownRevisionNewestFirst()
    {
        var (host, views) = await StartAsync();
        await using var _ = host;
        await using var __ = views;
        await using var connection = await views.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);

        var corrections = await new CorrectionHistoryStore(views).ListAsync(transaction, PublishedRecordKind.Vote, Id, 3, CancellationToken.None);

        Assert.Equal(History, corrections);
    }

    [Fact]
    public async Task APageReadOutsideATransactionAnswersNullWhenTheActiveReleaseIsNoLongerThePageRelease()
    {
        var (host, views) = await StartAsync();
        await using var _ = host;
        await using var __ = views;
        var store = new CorrectionHistoryStore(views);

        Assert.Equal(History, await store.ListAsync(PublishedRecordKind.Vote, Id, 1, 3, CancellationToken.None));
        Assert.Null(await store.ListAsync(PublishedRecordKind.Vote, Id, 2, 3, CancellationToken.None));
    }

    [Fact]
    public async Task APageReadOfARevisionThatTheViewDoesNotHoldFailsInsteadOfShowingNoBanner()
    {
        var (host, views) = await StartAsync();
        await using var _ = host;
        await using var __ = views;
        var store = new CorrectionHistoryStore(views);
        await using var connection = await views.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(transaction, PublishedRecordKind.Vote, Id, 2, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(PublishedRecordKind.Vote, Id, 1, 2, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(PublishedRecordKind.Vote, new string('b', 64), 1, 1, CancellationToken.None));
    }
}
