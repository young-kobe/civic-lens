using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRecordKindStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PostgresRecordKindStore store = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = new PostgresRecordKindStore(AnalysisDbContextFactory.Create(connectionString));
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARecordIdFindsTheKindThatOwnsItEvenForAStatementCandidateWithNoDraftAndAnUnknownIdFindsNone()
    {
        var ids = Enum.GetValues<PublishedRecordKind>().ToDictionary(kind => kind, IdOf);
        foreach (var kind in ids.Keys.Where(kind => kind != PublishedRecordKind.Statement))
            await seeder.ExecuteAsync(DraftRowInserts.For(kind), ("id", ids[kind]));
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
            VALUES (@id, 'source', 'https://example.test/statement', @extraction, 'attempt', 1)
            """, ("id", ids[PublishedRecordKind.Statement]), ("extraction", new string('e', 64)));

        foreach (var (kind, id) in ids)
            Assert.Equal(kind, await store.FindKindAsync(id, default));
        Assert.Null(await store.FindKindAsync(new string('9', 64), default));
        Assert.Null(await store.FindKindAsync(new string('9', 32), default));
    }

    private static string IdOf(PublishedRecordKind kind) =>
        new("abcdef0"[(int)kind], kind == PublishedRecordKind.DocumentChange ? 32 : 64);
}
