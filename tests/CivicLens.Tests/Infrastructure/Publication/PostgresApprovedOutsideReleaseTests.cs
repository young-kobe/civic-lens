using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresApprovedOutsideReleaseTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresPublicationStore store = null!;

    public static TheoryData<string> DraftTables => new(ApprovalDrafts.Inserts.Keys);

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        await ExecuteAsync(ApprovalDrafts.DropForeignKeys);
    }

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARecordThatIsNotApprovedDoesNotCountBecauseOnlyApprovedRecordsWaitForRoom()
    {
        foreach (var insert in ApprovalDrafts.Inserts.Values) await ExecuteAsync(insert);

        Assert.Equal(0, await store.CountApprovedOutsideActiveReleaseAsync([], default));
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ARecordHeldByTheActiveReleaseDoesNotCountBecauseItAlreadyHasASlot(string table)
    {
        await ExecuteAsync(ApprovalDrafts.Inserts[table].Replace("'AwaitingReview'", "'Approved'"));
        Assert.Equal(1, await store.CountApprovedOutsideActiveReleaseAsync([], default));
        var (kind, recordId) = ApprovalDrafts.Records[table];

        await ExecuteAsync($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('held'), 'owner', 0, 1, true);
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release) VALUES ('{kind}', '{recordId}', 0, 1);
            """);

        Assert.Equal(0, await store.CountApprovedOutsideActiveReleaseAsync([], default));
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task AWithdrawnRecordDoesNotCountEvenWithANewApprovedRevisionBecauseItNeverPublishesAgain(string table)
    {
        await ExecuteAsync(ApprovalDrafts.Inserts[table].Replace("'AwaitingReview'", "'Approved'"));
        var (kind, recordId) = ApprovalDrafts.Records[table];
        var withdrawn = PublishedRecordReference.Parse($"{kind}:{recordId}");

        Assert.Equal(1, await store.CountApprovedOutsideActiveReleaseAsync([], default));
        Assert.Equal(0, await store.CountApprovedOutsideActiveReleaseAsync([withdrawn], default));
    }

    [Fact]
    public async Task AWithdrawalOfOneKindLeavesTheSameIdOfAnotherKindCountedBecauseIdsAreUniquePerKind()
    {
        await ExecuteAsync(ApprovalDrafts.Inserts["action_drafts"].Replace("'AwaitingReview'", "'Approved'"));
        await ExecuteAsync(ApprovalDrafts.Inserts["brief_drafts"].Replace("'AwaitingReview'", "'Approved'"));

        var count = await store.CountApprovedOutsideActiveReleaseAsync([new(PublishedRecordKind.Brief, ApprovalDrafts.RecordId)], default);

        Assert.Equal(1, count);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
