using System.Globalization;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublishingApprovalNotifyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public static TheoryData<string> DraftTables => new(ApprovalDrafts.Inserts.Keys);

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ExecuteAsync(ApprovalDrafts.DropForeignKeys);
    }

    public async Task DisposeAsync() => await postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ApprovingARecordSendsTheGroupingDeadlineBecauseTheWorkerPublishesOnlyAfterTheGroupingWindow(string table)
    {
        await ExecuteAsync(ApprovalDrafts.Inserts[table]);
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
        await ExecuteAsync(ApprovalDrafts.Inserts[table].Replace("'AwaitingReview'", "'Approved'"));

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task ANewRevisionOfAnApprovedRecordSendsTheDeadlineBecauseTheStatusStaysApprovedWhenCodeApprovesIt(string table)
    {
        await ExecuteAsync(ApprovalDrafts.Inserts[table].Replace("'AwaitingReview'", "'Approved'"));
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        var before = TicksNow();
        await ExecuteAsync($"UPDATE {table} SET current_revision_number = 2");

        AssertGroupingDeadline(Assert.Single(await notifications.DrainAsync()), before);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task OnlyAnApprovedRevisionOrStatusNotifiesBecauseOtherChangesLeaveNothingToPublish(string table)
    {
        await ExecuteAsync(ApprovalDrafts.Inserts[table]);
        await using var notifications = await PublishingNotifications.ListenAsync(connectionString);

        await ExecuteAsync($"UPDATE {table} SET review_status = 'ChangesRequested'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'AwaitingReviewWithConcerns', review_state_version = 1");
        await ExecuteAsync($"UPDATE {table} SET current_revision_number = 2");
        Assert.Empty(await notifications.DrainAsync());

        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved', review_state_version = 2");
        Assert.Single(await notifications.DrainAsync());
    }

    // The test shortens the grouping window of the real function. The channel name stays the one the migration wrote.
    [Fact]
    public async Task ApprovingADraftWakesThePublishingWakeupBecauseTheTriggerAndTheWakeupMustShareOneChannel()
    {
        await ExecuteAsync("""
            DO $$
            BEGIN
                EXECUTE replace(pg_get_functiondef('civic_lens_notify_publishing_approval'::regproc), '60 seconds', '1 seconds');
            END $$;
            """);
        await using var wakeup = PostgresPublishingWakeup.FromConnectionString(connectionString, TimeSpan.FromHours(1));
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await ExecuteAsync(ApprovalDrafts.Inserts["executive_action_drafts"].Replace("'AwaitingReview'", "'Approved'"));

        await wakeup.WaitAsync(timeout.Token);
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
