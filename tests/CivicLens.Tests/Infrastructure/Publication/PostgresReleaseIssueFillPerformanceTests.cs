using System.Diagnostics;
using System.Reflection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseIssueFillPerformanceTests(PostgresCollection postgres, ITestOutputHelper output) : IAsyncLifetime
{
    private const int Bills = 15000;
    private const int Issues = 20;
    private const int Officials = 600;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // The seed runs ANALYZE, as autovacuum does soon after a bulk load. The existing 15k test runs none. Without statistics the Home counts took about 6.8 s on a dev run. Three runs with statistics took 0.51, 0.51, and 0.53 s for both fills. The budget is about twice the worst run.
    [Fact]
    public async Task TheIssueCountsAndThePartyLineUpFillFifteenThousandBillsForTwentyIssuesAndSixHundredOfficialsWithinTheBudget()
    {
        await SeedAsync();
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var counts = await TimeAsync(() => FillAsync(db, "ReleaseIssueCounts"));
        var lineUp = await TimeAsync(() => FillAsync(db, "ReleaseIssuePartyCounts"));
        await transaction.CommitAsync();

        output.WriteLine($"Issue counts {counts.TotalMilliseconds:F0} ms. Party line-up {lineUp.TotalMilliseconds:F0} ms. Budget {Budget.TotalMilliseconds:F0} ms.");
        Assert.True(counts + lineUp < Budget, $"The issue fills took {counts} and {lineUp}.");
        Assert.Equal([$"{Issues}", $"{Bills + Bills / 3}"], (await ReadAsync("""
            SELECT count(*)::text, sum(bills)::text FROM release_issues WHERE release_number = 1 AND bills > 0
            """)).Single());
        Assert.Equal(["0"], (await ReadAsync("""
            SELECT count(*)::text FROM release_issues i JOIN release_issue_party_lineups l USING (release_number, issue_id)
             WHERE i.bills <> l.unknown_bills + coalesce((SELECT sum(c.bills) FROM release_issue_party_counts c
                                                           WHERE c.release_number = l.release_number AND c.issue_id = l.issue_id), 0)
            """)).Single());
    }

    private static async Task FillAsync(CollectionAttemptDbContext db, string type)
    {
        var fill = typeof(PostgresPublicationStore).Assembly.GetType($"CivicLens.Infrastructure.Publication.{type}", throwOnError: true)!
            .GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!;
        await (Task)fill.Invoke(null, [db, 1, (int?)119, CancellationToken.None])!;
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var clock = Stopwatch.StartNew();
        await action();
        return clock.Elapsed;
    }

    // Each bill has one tracked issue. Every third bill has a second issue, and every fifth bill a second sponsor.
    // Each official has two terms, and the second term of every other official has the other party.
    private async Task SeedAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var statement in $"""
            SET session_replication_role = replica;
            INSERT INTO release_issues (release_number, issue_id, name, definition)
            SELECT 1, 'issue' || i, 'Issue ' || i, 'Definition' FROM generate_series(0, {Issues - 1}) i;
            INSERT INTO release_officials (release_number, official_id, name)
            SELECT 1, 'p' || o, 'Official ' || o FROM generate_series(0, {Officials - 1}) o;
            INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
            SELECT 1, 'p' || o, t, 'Representative', 'Representative', 'TX', 1,
                   CASE WHEN (o + t * o) % 2 = 0 THEN 'Republican' ELSE 'Democratic' END, 'Republican',
                   CASE t WHEN 0 THEN DATE '2023-01-03' ELSE DATE '2025-01-03' END,
                   CASE t WHEN 0 THEN DATE '2024-12-31' ELSE NULL END
              FROM generate_series(0, {Officials - 1}) o, generate_series(0, 1) t;
            {PublicActionRows.ActionRevision("template", 1, "Person", "2025-02-01", 1)}
            CREATE TEMP TABLE bulk_ids AS
            SELECT n, md5(n::text) || md5('x' || n::text) AS id FROM generate_series(1, {Bills}) n;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            SELECT 'action', id, 1, 1, NULL FROM bulk_ids;
            INSERT INTO action_revisions
            SELECT x.*
              FROM action_revisions r, bulk_ids b,
                   LATERAL jsonb_populate_record(NULL::action_revisions,
                       to_jsonb(r) || jsonb_build_object('record_id', b.id, 'bill_number', b.n, 'run_id', NULL, 'headline', 'Headline ' || b.n)) x
             WHERE r.record_id = 'template';
            DELETE FROM action_revisions WHERE record_id = 'template';
            INSERT INTO action_revision_issues (record_id, revision_number, issue_id, "position", is_tracked)
            SELECT id, 1, 'issue' || (n % {Issues}), 0, true FROM bulk_ids
            UNION ALL
            SELECT id, 1, 'issue' || ((n + 7) % {Issues}), 1, true FROM bulk_ids WHERE n % 3 = 0;
            INSERT INTO action_revision_sponsorships (record_id, revision_number, "position", person_id, bioguide_id, role, is_original, sponsored_on)
            SELECT id, 1, 0, 'p' || (n % {Officials}), 'Bp' || (n % {Officials}), 'Sponsor', true, DATE '2025-02-01' FROM bulk_ids
            UNION ALL
            SELECT id, 1, 1, 'p' || ((n + 1) % {Officials}), 'Bp' || ((n + 1) % {Officials}), 'Sponsor', false, DATE '2025-02-01' FROM bulk_ids WHERE n % 5 = 0;
            ANALYZE release_issues, release_official_terms, publication_record_states, action_revisions, action_revision_issues, action_revision_sponsorships;
            """.Split(";\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var clock = Stopwatch.StartNew();
            await using var command = new NpgsqlCommand(statement, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync();
            output.WriteLine($"seed {clock.ElapsedMilliseconds} ms: {statement.TrimStart()[..30]}");
        }
    }

    private async Task<string[][]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetString)]);
        return [.. rows];
    }
}
