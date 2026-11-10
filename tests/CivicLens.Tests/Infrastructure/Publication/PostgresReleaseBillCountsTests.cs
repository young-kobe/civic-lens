using System.Reflection;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseBillCountsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task FillUsesExactReleaseRangesAndCongressCoverageAndStoresOnlyNonzeroMemberRows()
    {
        await AddReleaseAsync(1, Published);
        await AddReleaseAsync(2, Published.AddDays(1));
        await AddReleaseAsync(3, Published.AddDays(2));
        await AddReleaseOfficialsAsync(1, "p1", "p2", "p3");
        await AddReleaseOfficialsAsync(2, "p1", "p2", "p3");
        await AddReleaseOfficialsAsync(3, "p1", "p2", "p3");
        await AddActionRevisionAsync(Action('a', 119, [Sponsor("p1"), Cosponsor("p1"), Cosponsor("p2"), Cosponsor("p3") with
            { WithdrawnOn = new DateOnly(2026, 2, 1) }]), 1);
        await AddActionRevisionAsync(Action('b', 118, [Sponsor("p1")]), 1, 1);
        await AddActionRevisionAsync(Action('c', 119, [Cosponsor("p2"), Sponsor("p4")]), 2);
        await AddActionRevisionAsync(Action('b', 118, [Sponsor("p2")], revision: 2), 3);
        await ViewsSeed.AddVotesAsync(connectionString,
            new SeededVote('d', "2026-05-05") { Congress = 118, FirstRelease = 2 },
            new SeededVote('e', "2026-05-06") { Congress = 120, FirstRelease = 2 });

        await FillAsync(1, 119);
        await FillAsync(2, 119);
        await FillAsync(3, 119);

        Assert.Equal(["118|1|1", "119|1|2"], await TotalsAsync(1));
        Assert.Equal(["118|0|0", "119|2|2", "120|0|0"], await TotalsAsync(2));
        Assert.Equal(["118|1|1", "119|2|2", "120|0|0"], await TotalsAsync(3));
        Assert.Equal(["118|p1|1|0", "119|p1|1|0", "119|p2|0|1"], await MembersAsync(1));
        Assert.Equal(["119|p1|1|0", "119|p2|0|2"], await MembersAsync(2));
        Assert.Equal(["118|p2|1|0", "119|p1|1|0", "119|p2|0|2"], await MembersAsync(3));
    }

    [Fact]
    public async Task EmptyFallbackCongressHasACompleteZeroSnapshot()
    {
        await AddReleaseAsync(1, Published);
        await AddReleaseOfficialsAsync(1, "p1", "p2");

        await FillAsync(1, 119);

        Assert.Equal(["119|0|0"], await TotalsAsync(1));
        Assert.Empty(await MembersAsync(1));
    }

    [Fact]
    public async Task RepeatedFillAcceptsExactRowsButRefusesAndPreservesChangedRows()
    {
        await AddReleaseAsync(1, Published);
        await AddReleaseOfficialsAsync(1, "p1");
        await AddActionRevisionAsync(Action('a', 119, [Sponsor("p1")]), 1);
        await FillAsync(1, 119);
        await FillAsync(1, 119);

        await ExecuteAsync("UPDATE release_bill_counts SET bills = 0 WHERE release_number = 1 AND congress = 119");
        await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Equal(["119|0|1"], await TotalsAsync(1));
    }

    [Fact]
    public async Task RepeatedFillDoesNotRepairADeletedSparseChildRow()
    {
        await AddReleaseAsync(1, Published);
        await AddReleaseOfficialsAsync(1, "p1");
        await AddActionRevisionAsync(Action('a', 119, [Sponsor("p1")]), 1);
        await FillAsync(1, 119);
        await ExecuteAsync("DELETE FROM release_official_bill_counts WHERE release_number = 1 AND official_id = 'p1'");

        await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Empty(await MembersAsync(1));
    }

    [Fact]
    public async Task SelectedActionBindingRequiresItsParentRevision()
    {
        await AddReleaseAsync(1, Published);
        var action = ActionRevisionFixtures.Model() with { Sponsorships = [ActionRevisionFixtures.Sponsorship() with { PersonId = "p1" }] };
        await ActionRevisionSeeder.AddAsync(connectionString, action);
        await ViewsSeed.ExecuteAsync(connectionString, $"SET session_replication_role = replica; INSERT INTO publication_record_states (kind, record_id, first_release, revision_number) VALUES ('action', '{action.RecordId}', 1, 1); DELETE FROM action_revisions WHERE record_id = '{action.RecordId}'");

        var refusal = await Assert.ThrowsAsync<InvalidDataException>(() => FillAsync(1, 119));

        Assert.Contains("not stored", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await TotalsAsync(1));
    }

    [Fact]
    public async Task SelectedVoteBindingRequiresItsParentRevision()
    {
        await AddReleaseAsync(1, Published);
        await ViewsSeed.AddVotesAsync(connectionString, new SeededVote('f', "2026-05-01"));
        await ViewsSeed.ExecuteAsync(connectionString, $"SET session_replication_role = replica; DELETE FROM vote_revisions WHERE record_id = '{new string('f', 64)}'");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Contains("selected vote revision", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await TotalsAsync(1));
    }

    [Fact]
    public async Task ActiveCosponsorProjectionMismatchRefusesTheSnapshot()
    {
        await AddReleaseAsync(1, Published);
        var action = ActionRevisionFixtures.Model() with
        {
            Sponsorships = [ActionRevisionFixtures.Sponsorship() with { PersonId = "p1" }, ActionRevisionFixtures.Sponsorship() with
                { PersonId = "p2", Role = ActionSponsorshipRole.Cosponsor }]
        };
        await ActionRevisionSeeder.AddAsync(connectionString, action);
        await ExecuteAsync($"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number) VALUES ('action', '{action.RecordId}', 1, 1); DELETE FROM action_revision_sponsorship_citations WHERE record_id = '{action.RecordId}' AND sponsorship_position = 1; DELETE FROM action_revision_sponsorships WHERE record_id = '{action.RecordId}' AND position = 1");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Contains("mismatched bill count projection", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await TotalsAsync(1));
    }

    [Fact]
    public async Task ActionWithMissingSponsorshipCitationRefusesTheSnapshot()
    {
        await AddReleaseAsync(1, Published);
        var action = Action('a', 119, [Sponsor("p1")]);
        await ActionRevisionSeeder.AddAsync(connectionString, action);
        await ExecuteAsync($"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number) VALUES ('action', '{action.RecordId}', 1, 1); DELETE FROM action_revision_sponsorship_citations WHERE record_id = '{action.RecordId}'");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Contains("cannot produce a valid bill count projection", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await TotalsAsync(1));
    }

    [Fact]
    public async Task IncompleteActionProjectionRefusesTheSnapshotBeforeWritingAnyRows()
    {
        await AddReleaseAsync(1, Published);
        await AddReleaseOfficialsAsync(1, "p1");
        await AddActionRevisionAsync(Action('a', 119, [Sponsor("p1")]), 1);
        await ExecuteAsync($"UPDATE action_revisions SET event_on = NULL, cosponsor_count = NULL WHERE record_id = '{new string('a', 64)}'");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => FillAsync(1, 119));

        Assert.Contains("mismatched bill count projection", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await TotalsAsync(1));
        Assert.Empty(await MembersAsync(1));
    }

    [Fact]
    public async Task ACountSnapshotFailureRollsBackTheReleaseAndBothBillCountTables()
    {
        await ExecuteAsync("""
            CREATE FUNCTION fail_release_bill_count_write() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                RAISE EXCEPTION 'forced release bill count failure';
            END;
            $function$;
            CREATE TRIGGER fail_release_bill_count_write BEFORE INSERT ON release_bill_counts
                FOR EACH ROW EXECUTE FUNCTION fail_release_bill_count_write();
            """);
        var commit = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            Names = ReleaseNameFixtures.None
        };

        var failure = await Assert.ThrowsAsync<PostgresException>(() => publications.CommitAsync(
            "auth0|owner", commit, "bill-count-rollback", new string('a', 64), default));

        Assert.Equal("P0001", failure.SqlState);
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM publication_releases"));
        Assert.Empty(await ReadAsync("SELECT release_number::text FROM release_officials"));
        Assert.Empty(await TotalsAsync(1));
        Assert.Empty(await MembersAsync(1));
    }

    private static ActionDraftRevision Action(char id, int congress, ActionSponsorship[] sponsorships, int revision = 1)
    {
        var revisionId = new string(id, 64);
        return ActionRevisionFixtures.Model(revision) with
        {
            RecordId = revisionId,
            CandidateId = new string('c', 64),
            Fingerprint = new string('d', 64),
            Congress = congress,
            BillNumber = id,
            Sponsorships = [.. sponsorships]
        };
    }

    private static ActionSponsorship Sponsor(string personId) => ActionRevisionFixtures.Sponsorship() with
    { PersonId = personId, Role = ActionSponsorshipRole.Sponsor };

    private static ActionSponsorship Cosponsor(string personId) => ActionRevisionFixtures.Sponsorship() with
    { PersonId = personId, Role = ActionSponsorshipRole.Cosponsor };

    private async Task AddActionRevisionAsync(ActionDraftRevision revision, int firstRelease, int? lastRelease = null)
    {
        await ActionRevisionSeeder.AddAsync(connectionString, revision);
        var last = lastRelease?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
        await ExecuteAsync($"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('action', '{revision.RecordId}', {firstRelease}, {revision.RevisionNumber}, {last})");
    }

    private async Task AddReleaseAsync(int release, DateTimeOffset publishedAt) =>
        await ViewsSeed.ActivateReleaseAsync(connectionString, release, publishedAt);

    private Task AddReleaseOfficialsAsync(int release, params string[] officialIds) => ExecuteAsync(string.Join('\n', officialIds.Select(id => $"""
        INSERT INTO release_officials (release_number, official_id, name) VALUES ({release}, '{id}', '{id}');
        """)));

    private async Task FillAsync(int release, int congress)
    {
        var bindings = await SelectedBindingsAsync(release);
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await FillInTransactionAsync(db, release, congress, bindings);
        await transaction.CommitAsync();
    }

    private async Task<PublishedRevisionBinding[]> SelectedBindingsAsync(int release)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT kind, record_id, revision_number FROM publication_record_states
             WHERE kind IN ('action', 'vote') AND first_release <= @release AND (last_release IS NULL OR last_release >= @release)
            """, connection);
        command.Parameters.AddWithValue("release", release);
        await using var reader = await command.ExecuteReaderAsync();
        var bindings = new List<PublishedRevisionBinding>();
        while (await reader.ReadAsync())
        {
            var kind = reader.GetString(0) == "action" ? PublishedRecordKind.Action : PublishedRecordKind.Vote;
            bindings.Add(new(kind, reader.GetString(1), reader.GetInt32(2)));
        }
        return [.. bindings];
    }

    private static async Task FillInTransactionAsync(CollectionAttemptDbContext db, int release, int congress, PublishedRevisionBinding[] bindings)
    {
        var type = typeof(PostgresPublicationStore).Assembly.GetType("CivicLens.Infrastructure.Publication.ReleaseBillCounts", throwOnError: true)!;
        var method = type.GetMethod("FillAsync", BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [db, release, (int?)congress, bindings, CancellationToken.None])!;
        await task;
    }

    private CollectionAttemptDbContext NewContext() => new(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
        .UseNpgsql(connectionString).Options);

    private Task<string[]> TotalsAsync(int release) => ReadAsync($"""
        SELECT congress || '|' || bills || '|' || expected_member_row_count
          FROM release_bill_counts WHERE release_number = {release} ORDER BY congress
        """);

    private Task<string[]> MembersAsync(int release) => ReadAsync($"""
        SELECT congress || '|' || official_id || '|' || sponsored || '|' || cosponsored
          FROM release_official_bill_counts WHERE release_number = {release}
         ORDER BY congress, official_id
        """);

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
