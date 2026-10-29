using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresMemberRosterStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresMemberRosterStore roster = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        roster = PostgresMemberRosterStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACaptureWritesOneSnapshotOfMembersAndDatedTermsThatCiteItsAttempt()
    {
        var attemptId = await ImportAsync('a', 0);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default);

        var latest = await roster.ReadLatestAsync(RosterFixtures.SourceId, default);
        Assert.Equal(["C000127", "N000147", "W000832"], latest.Select(member => member.BioguideId));
        var cantwell = latest[0];
        Assert.Equal(("S275", "511 Hart Senate Office Building"), (cantwell.LisId, cantwell.Office));
        Assert.Equal([new DateOnly(1993, 1, 5), new DateOnly(2025, 1, 3)], cantwell.Terms.Select(term => term.Start));
        Assert.Equal((3L, 5L), (await CountAsync("roster_members", attemptId), await CountAsync("roster_terms", attemptId)));
        Assert.Equal(1L, await CountAsync("roster_snapshots", attemptId));
    }

    [Fact]
    public async Task ATermCaucusRoundTripsSoAnIndependentComparesWithTheCaucusParty()
    {
        var attemptId = await ImportAsync('a', 0);
        var cantwell = RosterFixtures.Members()[0];
        var independent = cantwell with { Terms = [cantwell.Terms[0], cantwell.Terms[1] with { Caucus = "Democrat" }] };

        await roster.AdmitAsync(attemptId, [independent], null, default);

        var latest = await roster.ReadLatestAsync(RosterFixtures.SourceId, default);
        Assert.Equal([null, "Democrat"], latest[0].Terms.Select(term => term.Caucus));
    }

    [Fact]
    public async Task ASnapshotOfOneSourceIsNotKnownToAnotherSourceThatCapturedTheSameBody()
    {
        var attemptId = await ImportAsync('a', 0);
        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default);
        var otherSource = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning.AddDays(1), "other-members");

        Assert.False(await roster.HasSnapshotOfCaptureAsync(otherSource, default));
        Assert.Empty(await roster.ReadLatestAsync("other-members", default));
    }

    [Fact]
    public async Task AMemberRowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt()
    {
        var attemptId = await ImportAsync('a', 0);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO roster_members (attempt_id, bioguide_id, full_name) VALUES ('{attemptId}', 'A000001', 'A')
            """));

        Assert.Equal("FK_roster_members_roster_snapshots_attempt_id", failure.ConstraintName);
    }

    [Fact]
    public async Task ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        await roster.AdmitAsync(a, RosterFixtures.Members(), null, default);
        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], null, default);
        var aAgain = await ImportAsync('a', 2);
        var newBody = await ImportAsync('c', 3);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));
        Assert.False(await roster.HasSnapshotOfCaptureAsync(newBody, default));
    }

    [Fact]
    public async Task AReplayOfAnAdmittedAttemptMustSkipOrItHitsAPrimaryKeyConflictAndEndsFailed()
    {
        var attemptId = await ImportAsync('a', 0);
        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(attemptId, default));
    }

    [Fact]
    public async Task ReaderFollowsTheNewestCaptureWhateverTheAdmissionOrder()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        var aAgain = await ImportAsync('a', 2);
        await roster.AdmitAsync(a, RosterFixtures.Members(), null, default);
        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));

        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], null, default);

        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task AFileThatChangesAndThenChangesBackReadsAsTheEarlierSnapshotWithoutANewOne()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        await roster.AdmitAsync(a, RosterFixtures.Members(), null, default);
        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], null, default);
        Assert.Equal(2, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        var aAgain = await ImportAsync('a', 2);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));
        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task TwoSnapshotsOfOneBodyReadAsTheOldestOne()
    {
        var first = await ImportAsync('a', 0);
        var raced = await ImportAsync('a', 1);
        await roster.AdmitAsync(raced, [.. RosterFixtures.Members().Take(1)], null, default);
        await roster.AdmitAsync(first, RosterFixtures.Members(), null, default);

        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 1);
        await roster.AdmitAsync(first, RosterFixtures.Members(), null, default);

        await roster.AdmitAsync(changed, [.. RosterFixtures.Members().Take(2)], null, default);

        Assert.Equal(2, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.Equal(3, await CountAsync("roster_members", first));
    }

    [Fact]
    public async Task AFailedInsertLeavesNoPartialSnapshot()
    {
        var attemptId = await ImportAsync('a', 0);
        var members = RosterFixtures.Members();
        var broken = members[1] with { Terms = [members[1].Terms[0] with { Chamber = "gov" }] };
        await SeedBlockedVoteAsync('1', "Senate", "evidenceUnavailable");

        await Assert.ThrowsAsync<DbUpdateException>(() => roster.AdmitAsync(attemptId, [members[0], broken], null, default));

        Assert.Equal(["Blocked"], await StatusesAsync());

        Assert.Equal((0L, 0L, 0L), (await CountAsync("roster_members", attemptId), await CountAsync("roster_terms", attemptId),
            await CountAsync("roster_snapshots", attemptId)));
    }

    [Fact]
    public async Task SenatorLisIdsListOnlySenatorsFromEverySnapshotSoASenatorWhoLeftStillMatchesHisOldVotes()
    {
        var first = await ImportAsync('a', 0);
        var later = await ImportAsync('b', 1);
        await roster.AdmitAsync(first, RosterFixtures.Members(), null, default);
        await roster.AdmitAsync(later, [.. RosterFixtures.Members().Skip(1)], null, default);

        var senators = await roster.ReadSenatorLisIdsAsync(RosterFixtures.SourceId, default);

        Assert.Equal([new SenatorLisId("C000127", "S275")], senators);
        Assert.DoesNotContain(await roster.ReadLatestAsync(RosterFixtures.SourceId, default), member => member.BioguideId == "C000127");
    }

    [Fact]
    public async Task ASenatorWithTwoLisIdsInTwoSnapshotsListsBothSoTheCallerCanRefuseTheConflict()
    {
        var first = await ImportAsync('a', 0);
        var later = await ImportAsync('b', 1);
        var members = RosterFixtures.Members();
        await roster.AdmitAsync(first, members, null, default);
        await roster.AdmitAsync(later, [members[0] with { LisId = "S999" }], null, default);

        var senators = await roster.ReadSenatorLisIdsAsync(RosterFixtures.SourceId, default);

        Assert.Equal([new SenatorLisId("C000127", "S275"), new SenatorLisId("C000127", "S999")], senators);
    }

    [Fact]
    public async Task ASenatorWithoutALisIdListsWithANullIdSoTheCallerCanBlockInsteadOfSkippingHim()
    {
        var attemptId = await ImportAsync('a', 0);
        var members = RosterFixtures.Members();
        await roster.AdmitAsync(attemptId, [members[0] with { LisId = null }], null, default);

        Assert.Equal([new SenatorLisId("C000127", null)], await roster.ReadSenatorLisIdsAsync(RosterFixtures.SourceId, default));
    }

    [Fact]
    public async Task SenatorLisIdsOfOneSourceExcludeTheSnapshotsOfAnotherSource()
    {
        var attemptId = await ImportAsync('a', 0);
        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default);

        Assert.Empty(await roster.ReadSenatorLisIdsAsync("other-members", default));
    }

    [Fact]
    public async Task NoSnapshotReadsAsEmpty()
    {
        Assert.Empty(await roster.ReadLatestAsync(RosterFixtures.SourceId, default));
    }

    [Fact]
    public async Task AnUnknownAttemptIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => roster.HasSnapshotOfCaptureAsync("missing", default));
    }

    [Fact]
    public async Task TheDailyCheckCreatesARosterJobWithNoCredentialEvenThoughTheSourceCoversNoPeople()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        await jobs.SynchronizeAsync(RosterFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        Assert.Equal(1, await jobs.AdvanceDueAsync(10, default));

        var job = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal((ApiResource.MemberRoster, RosterFixtures.SourceUrl, null), (job.Definition.ApiResource, job.Definition.Url,
            job.Definition.Credential));
    }

    [Fact]
    public async Task ANewSnapshotRequeuesOnlyTheSenateDraftsBlockedAsEvidenceUnavailableBecauseANewSenatorMayNowMatch()
    {
        await SeedBlockedVoteAsync('1', "Senate", "evidenceUnavailable");
        await SeedBlockedVoteAsync('2', "House", "evidenceUnavailable");
        await SeedBlockedVoteAsync('3', "Senate", "tooManyMembers");

        await roster.AdmitAsync(await ImportAsync('a', 0), RosterFixtures.Members(), null, default);

        Assert.Equal(["Pending", "Blocked", "Blocked"], await StatusesAsync());
    }

    [Fact]
    public async Task ANewSnapshotLeavesTheSenateDraftsOfADisabledSourceBlockedSoASwitchedSourceNeverDraftsAgain()
    {
        await SeedBlockedVoteAsync('1', "Senate", "evidenceUnavailable");
        await SeedBlockedVoteAsync('2', "Senate", "evidenceUnavailable", sourceId: "old-votes");
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO collection_source_schedules (source_id, configuration_revision_id, interval_seconds, next_due_utc_ticks, enabled)
            VALUES ('old-votes', '{new string('c', 64)}', 3600, 0, false), ('votes', '{new string('c', 64)}', 3600, 0, true)
            """);

        await roster.AdmitAsync(await ImportAsync('a', 0), RosterFixtures.Members(), null, default);

        Assert.Equal(["Pending", "Blocked"], await StatusesAsync());
    }

    private Task SeedBlockedVoteAsync(char id, string chamber, string errorCode, string sourceId = "votes") => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks,
            detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES ('{new string(id, 64)}', '{sourceId}', '{chamber}', 119, 1, {id}, 1, 'detail-{id}', 'members-{id}',
                '{new string('c', 64)}', '{new string('c', 64)}', '{new string('c', 64)}', 1);
        INSERT INTO vote_analysis (candidate_id, task, status, error_code, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES ('{new string(id, 64)}', 'vote-draft', 'Blocked', '{errorCode}', 0, 1, 0, 1)
        """);

    private async Task<string[]> StatusesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM vote_analysis ORDER BY candidate_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var statuses = new List<string>();
        while (await reader.ReadAsync()) statuses.Add(reader.GetString(0));
        return [.. statuses];
    }

    private Task<string> ImportAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day));

    private async Task<long> CountAsync(string table, string attemptId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE attempt_id = @attempt", connection);
        command.Parameters.AddWithValue("attempt", attemptId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
