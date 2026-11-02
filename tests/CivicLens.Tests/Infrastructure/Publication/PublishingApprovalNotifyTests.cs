using System.Collections.Concurrent;
using System.Globalization;
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
        await using var listener = await ListenAsync();

        var before = TicksNow();
        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved'");
        var payloads = await ReadPayloadsAsync(listener);

        var deadline = long.Parse(Assert.Single(payloads), CultureInfo.InvariantCulture);
        Assert.InRange(deadline, before + TimeSpan.FromSeconds(55).Ticks, TicksNow() + TimeSpan.FromSeconds(61).Ticks);
    }

    [Theory]
    [MemberData(nameof(DraftTables))]
    public async Task OnlyTheChangeToApprovedNotifiesBecauseOtherStatusChangesLeaveNothingToPublish(string table)
    {
        await ExecuteAsync(Inserts[table]);
        await using var listener = await ListenAsync();

        await ExecuteAsync($"UPDATE {table} SET review_status = 'ChangesRequested'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'AwaitingReviewWithConcerns', review_state_version = 1");
        Assert.Empty(await ReadPayloadsAsync(listener));

        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved'");
        await ExecuteAsync($"UPDATE {table} SET review_status = 'Approved', review_state_version = 2");
        Assert.Single(await ReadPayloadsAsync(listener));
    }

    private async Task<NpgsqlConnection> ListenAsync()
    {
        var listener = new NpgsqlConnection(connectionString);
        await listener.OpenAsync();
        await using var command = new NpgsqlCommand("LISTEN civic_lens_publishing", listener);
        await command.ExecuteNonQueryAsync();
        return listener;
    }

    private static async Task<List<string>> ReadPayloadsAsync(NpgsqlConnection listener)
    {
        var payloads = new ConcurrentQueue<string>();
        listener.Notification += (_, eventArgs) => payloads.Enqueue(eventArgs.Payload);
        while (await listener.WaitAsync(500)) { }
        return [.. payloads];
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
