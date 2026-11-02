using System.Globalization;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublishingApprovalNotifyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string RecordId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ShortId = "0123456789abcdef0123456789abcdef";

    private static readonly Dictionary<string, string> Inserts = new()
    {
        ["action_drafts"] = $"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{RecordId}', 'congress-gov', 119, 'hr', 1, 1, 0, 'AwaitingReview', 0)
            """,
        ["brief_drafts"] = $"""
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', DATE '2026-01-05', 1, 0, 'AwaitingReview', 0, 0)
            """,
        ["document_change_drafts"] = $"""
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{ShortId}', 'comparison', 1, 0, 'AwaitingReview', 0)
            """,
        ["executive_action_drafts"] = $"""
            INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', 1, 0, 'AwaitingReview', 0, 0)
            """,
        ["relationship_drafts"] = $"""
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', 1, 0, 'AwaitingReview', false, 0, 0)
            """,
        ["statement_drafts"] = """
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('candidate', 1, 0, 'AwaitingReview', 0)
            """,
        ["vote_drafts"] = $"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{RecordId}', 'house-clerk', 'House', 119, 1, 1, 1, 0, 'AwaitingReview', 0)
            """
    };

    private string connectionString = null!;

    public static TheoryData<string> DraftTables => new(Inserts.Keys);

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ExecuteAsync("""
            ALTER TABLE action_drafts DROP CONSTRAINT "FK_action_drafts_action_candidates_candidate_id";
            ALTER TABLE vote_drafts DROP CONSTRAINT "FK_vote_drafts_vote_candidates_candidate_id";
            ALTER TABLE document_change_drafts DROP CONSTRAINT "FK_document_change_drafts_document_comparisons_comparison_id";
            ALTER TABLE statement_drafts DROP CONSTRAINT "FK_statement_drafts_statement_candidates_candidate_id";
            """);
    }

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ApprovingARecordSendsTheGroupingDeadlineBecauseTheWorkerPublishesOnlyAfterTheGroupingWindow(string table)
    {
        await ExecuteAsync(Inserts[table]);
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        var before = TicksNow();
        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved'");

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ANewRecordThatIsApprovedOnInsertSendsTheDeadlineBecauseCodeApprovesAFactRecordInItsFirstWrite(string table)
    {
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        var before = TicksNow();
        await ExecuteAsync(Inserts[table].Replace("'AwaitingReview'", "'Approved'"));

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ANewRevisionOfAnApprovedRecordSendsTheDeadlineBecauseTheStatusStaysApprovedWhenCodeApprovesIt(string table)
    {
        await ExecuteAsync(Inserts[table].Replace("'AwaitingReview'", "'Approved'"));
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        var before = TicksNow();
        await ExecuteAsync($"UPDATE {table} SET current_revision_number = 2");

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task OnlyAnApprovedRevisionOrStatusNotifiesBecauseOtherChangesLeaveNothingToPublish(string table)
    {
        await ExecuteAsync(Inserts[table]);
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        await ExecuteAsync($"UPDATE {table} SET review_status = 'ChangesRequested'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'AwaitingReviewWithConcerns', review_state_version = 1");
        await ExecuteAsync($"UPDATE {table} SET current_revision_number = 2");
        Assert.Empty(await notifications.DrainAsync());

        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved', review_state_version = 2");
        Assert.Single(await notifications.DrainAsync());
    }

    [Fact]
    public async Task ALoneApprovalAfterAQuietPeriodPublishesAboutAMinuteLaterBecauseTheLastReleaseIsOld()
    {
        await ExecuteAsync(Inserts["action_drafts"]);
        await InsertReleaseAsync(TimeSpan.FromHours(1));
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        var before = TicksNow();
        await ExecuteAsync("UPDATE action_drafts SET review_status = 'Approved'");

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Fact]
    public async Task ABurstAfterARecentReleaseWaitsUntilTenMinutesAfterThatReleaseBecausePublishesCostDisk()
    {
        await ExecuteAsync(Inserts["action_drafts"]);
        await ExecuteAsync(Inserts["vote_drafts"]);
        var releaseTicks = await InsertReleaseAsync(TimeSpan.FromMinutes(1));
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        await ExecuteAsync("UPDATE action_drafts SET review_status = 'Approved'");
        await ExecuteAsync("UPDATE vote_drafts SET review_status = 'Approved'");

        var payloads = await notifications.DrainAsync();
        Assert.Equal(2, payloads.Count);
        Assert.All(payloads, payload => Assert.Equal(releaseTicks + TimeSpan.FromMinutes(10).Ticks, long.Parse(payload, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ARestartedWorkerStillSeesTheGapBecauseTheLastReleaseIsStoredInTheDatabase()
    {
        await ExecuteAsync(Inserts["action_drafts"]);
        var releaseTicks = await InsertReleaseAsync(TimeSpan.FromMinutes(5));
        await using (await PublishingNotifications.ListenAsync(connectionString)) { }
        await using var restarted = await PublishingNotifications.ListenAsync(connectionString);

        await ExecuteAsync("UPDATE action_drafts SET review_status = 'Approved'");

        Assert.Equal(releaseTicks + TimeSpan.FromMinutes(10).Ticks, long.Parse(Assert.Single(await restarted.DrainAsync()), CultureInfo.InvariantCulture));
    }

    private async Task<long> InsertReleaseAsync(TimeSpan age)
    {
        var published = TicksNow() - age.Ticks;
        await ExecuteAsync($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-{ShortId}', 'owner', {published}, 1, true)
            """);
        return published;
    }

    private static void AssertGroupingDeadline(string payload, long before)
    {
        var deadline = long.Parse(payload, CultureInfo.InvariantCulture);
        Assert.InRange(deadline, before + TimeSpan.FromSeconds(55).Ticks, TicksNow() + TimeSpan.FromSeconds(61).Ticks);
    }

    private static long TicksNow() => DateTime.UtcNow.Ticks;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
