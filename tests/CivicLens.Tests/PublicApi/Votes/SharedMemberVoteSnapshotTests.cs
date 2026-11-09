using CivicLens.PublicApi.Votes;
using CivicLens.Tests.PublicApi;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SharedMemberVoteSnapshotTests(PostgresCollection postgres)
{
    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    [Fact]
    public async Task ReadsReleaseAndSelectedCastFromOneDatabaseSnapshot()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        using var gate = new FirstPublicApiReadGate(TimeSpan.FromSeconds(15));
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(gate).SetMinimumLevel(LogLevel.Trace));
        Task<SharedMemberVoteAnswer?>? read = null;
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2026-05-01")
                {
                    FirstRelease = 1,
                    LastRelease = 1,
                    RecordId = 1.ToString("x64"),
                    Casts = ["Yea"]
                },
                new SeededVote('b', "2026-04-01")
                {
                    FirstRelease = 1,
                    RecordId = 2.ToString("x64"),
                    Kind = "Rule",
                    Casts = ["Nay"]
                });
            await ViewsSeed.ExecuteAsync(database, $"""
                INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
                VALUES (2, '000002-{new string('0', 31)}1', 'test', {new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero).UtcTicks}, 1, false);
                """);
            await using (var owner = new NpgsqlConnection(database))
            {
                await owner.OpenAsync();
                await using var command = new NpgsqlCommand($"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'", owner);
                await command.ExecuteNonQueryAsync();
            }

            var publicConnection = new NpgsqlConnectionStringBuilder(database)
            {
                Username = PublicRole,
                Password = PublicPassword
            }.ConnectionString;
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(publicConnection);
            dataSourceBuilder.UseLoggerFactory(loggerFactory);
            await using var views = dataSourceBuilder.Build();
            var store = new MemberVoteStore(views);
            read = Task.Run(() => store.FindSharedAsync(new MemberVoteRequest { Officials = ["p0"] }, default));

            await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(15));
            await ViewsSeed.ActivateReleaseAsync(database, 2, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            gate.Resume();
            var answer = await read.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotNull(answer);
            Assert.Equal(1, answer.Release);
            Assert.Equal(["p0"], answer.OfficialIds);
            Assert.Single(answer.Votes);
            Assert.Single(answer.Votes[0].Casts);
            Assert.Equal("0000000000000000000000000000000000000000000000000000000000000001", answer.Votes[0].RecordId);
            Assert.Equal("Yea", answer.Votes[0].Casts[0].Cast);

        }
        finally
        {
            gate.Resume();
            if (read is not null)
            {
                try { await read.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch { }
            }
            try { await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE {PublicRole} NOLOGIN PASSWORD NULL"); }
            finally { await postgres.DropDatabaseAsync(database); }
        }
    }
}
