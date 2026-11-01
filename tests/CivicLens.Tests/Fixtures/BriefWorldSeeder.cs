using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Publication;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

// The brief selection reads only releases and the exact revision rows, so the seeder relaxes the foreign keys of the drafts around them.
internal sealed class BriefWorldSeeder(string connectionString)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public static string StatementId(char c) => new(c, 64);
    public static string ActionId(char c) => new(c, 64);
    public static string DocumentChangeId(char c) => new(c, 32);

    public async Task ReleaseAsync(int number, DateTimeOffset publishedAt, bool active, params PublishedRevisionBinding[] records)
    {
        await ReleaseWithoutApprovalsAsync(number, publishedAt, active, records);
        foreach (var record in records) await DecisionAsync(record.Kind, record.RecordId, record.RevisionNumber, record.RevisionNumber, ReviewDecisionKind.Approve);
    }

    public async Task ReleaseWithoutApprovalsAsync(int number, DateTimeOffset publishedAt, bool active, params PublishedRevisionBinding[] records)
    {
        await ExecuteAsync("""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (@number, @directory, 'auth0|owner', @ticks, @count, @active)
            """, ("number", number), ("directory", $"{number:D6}-{Guid.NewGuid():N}"), ("ticks", publishedAt.UtcTicks), ("count", records.Length),
            ("active", active));
        await ExecuteAsync("""
            WITH released AS (
                SELECT * FROM unnest(@kinds, @ids, @revisions) AS k(kind, record_id, revision_number)),
            closed AS (
                UPDATE publication_record_states s SET last_release = (SELECT max(release_number) FROM publication_releases WHERE release_number < @number)
                WHERE s.last_release IS NULL
                    AND NOT EXISTS (SELECT 1 FROM released r WHERE (r.kind, r.record_id, r.revision_number) = (s.kind, s.record_id, s.revision_number)))
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
            SELECT r.kind, r.record_id, r.revision_number, @number FROM released r
            WHERE NOT EXISTS (SELECT 1 FROM publication_record_states s
                WHERE s.last_release IS NULL AND (r.kind, r.record_id, r.revision_number) = (s.kind, s.record_id, s.revision_number))
            """, ("number", number), ("kinds", records.Select(record => PublishedRecordReference.NameOf(record.Kind)).ToArray()),
            ("ids", records.Select(record => record.RecordId).ToArray()), ("revisions", records.Select(record => record.RevisionNumber).ToArray()));
    }

    public Task DecisionAsync(PublishedRecordKind kind, string id, int revision, int version, ReviewDecisionKind decision)
    {
        var (table, column) = kind switch
        {
            PublishedRecordKind.Statement => ("statement_review_decisions", "candidate_id"),
            PublishedRecordKind.DocumentChange => ("document_change_review_decisions", "draft_id"),
            PublishedRecordKind.Brief => ("brief_review_decisions", "record_id"),
            PublishedRecordKind.Vote => ("vote_review_decisions", "record_id"),
            _ => ("action_review_decisions", "record_id")
        };
        return ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO {table} (decision_id, {column}, revision_number, review_state_version, decision_json, kind, actor_subject, created_at_utc_ticks)
            VALUES (md5(@id || '|' || @revision::text || '|' || @version::text), @id, @revision, @version, 'x', @kind, 'auth0|seed', 1)
            ON CONFLICT DO NOTHING
            """, ("id", id), ("revision", revision), ("version", version), ("kind", decision.ToString()));
    }

    public Task StatementAsync(string id, int revision, string headline, params string[] officials) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @revision, 'auth0|editor', 'Person', NULL, @json, 0)
        """, ("id", id), ("revision", revision), ("json", JsonSerializer.Serialize(new StatementDraftRevision(id, revision, "auth0|editor", Created,
            headline, "Summary of " + headline, new DateOnly(2026, 10, 6), null, [.. officials], [], [], [], [], []), Web)));

    public Task DocumentChangeAsync(string id, int revision, string headline, params string[] officials) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks)
        VALUES (@id, @revision, @json, 0)
        """, ("id", id), ("revision", revision), ("json", JsonSerializer.Serialize(new DocumentChangeDraftRevision(id, revision, new string('c', 64),
            "auth0|editor", Created, headline, "Summary of " + headline, null, null, "Agency", null, null, [.. officials], [], []), Web)));

    public Task ActionAsync(string id, int revision, int billNumber, params string[] sponsors) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @revision, 'auth0|editor', 'Person', NULL, @json, 0)
        """, ("id", id), ("revision", revision), ("json", JsonSerializer.Serialize(ActionRevisionFixtures.Model(revision) with
    {
        RecordId = id,
        BillNumber = billNumber,
        Headline = $"Bill {billNumber}",
        Sponsorships = [.. sponsors.Select(sponsor => ActionRevisionFixtures.Sponsorship() with { PersonId = sponsor })]
    }, Web)));

    public Task FinishedWeekAsync(DateOnly week) => ExecuteAsync("""
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-draft', @week, 'Succeeded', 0, 3, 0, 1)
        """, ("id", BriefWeek.RecordIdOf(week)), ("week", week));

    public Task BriefAsync(DateOnly week, string queueStatus = "Succeeded", string reviewStatus = "AwaitingReview") => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-draft', @week, @queue, 0, 3, 0, 1);
        INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, @week, 1, 0, @review, 1, 1);
        INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, 1, 'auth0|editor', 'Person', NULL, 'x', 1);
        """, ("id", BriefWeek.RecordIdOf(week)), ("week", week), ("queue", queueStatus), ("review", reviewStatus));

    public async Task ReleaseCitedRecordsAsync()
    {
        await StatementAsync(BriefFixtures.CitedStatement, 1, "Rent cap", "warren");
        await ActionAsync(BriefFixtures.CitedAction, 1, 7, "warren", "markey");
        await DocumentChangeAsync(BriefFixtures.CitedDocumentChange, 1, "A rule changed");
        await ReleaseAsync(1, BriefFixtures.InWeek, active: true, Binding(PublishedRecordKind.Statement, BriefFixtures.CitedStatement, 1),
            Binding(PublishedRecordKind.Action, BriefFixtures.CitedAction, 1),
            Binding(PublishedRecordKind.DocumentChange, BriefFixtures.CitedDocumentChange, 1));
    }

    public Task ReviewableBriefAsync(BriefRevision revision, string reviewStatus = "AwaitingReview") => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-draft', @week, 'Succeeded', 0, 3, 0, 1);
        INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, @week, @number, 0, @review, @created, @created);
        INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, @author, 'Analysis', substr(@id, 1, 32), @json, @created);
        """, ("id", revision.RecordId), ("week", revision.Window.WeekStart), ("number", revision.RevisionNumber),
        ("review", reviewStatus), ("created", revision.CreatedAtUtc.UtcTicks), ("author", revision.AuthorSubject),
        ("json", JsonSerializer.Serialize(revision, Web)));

    public Task AppendRevisionAsync(BriefRevision revision) => ExecuteAsync("""
        SET session_replication_role = replica;
        UPDATE brief_drafts SET current_revision_number = @number WHERE record_id = @id;
        INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, @author, 'Analysis', substr(md5(@id || @number::text), 1, 32), @json, @created);
        """, ("id", revision.RecordId), ("number", revision.RevisionNumber), ("created", revision.CreatedAtUtc.UtcTicks),
        ("author", revision.AuthorSubject), ("json", JsonSerializer.Serialize(revision, Web)));

    public async Task ReplaceActiveReleaseAsync(int number, DateTimeOffset publishedAt, params PublishedRevisionBinding[] records)
    {
        await ReleaseWithoutApprovalsAsync(number, publishedAt, active: false, records);
        await ExecuteAsync("UPDATE publication_releases SET is_active = false");
        await ExecuteAsync("UPDATE publication_releases SET is_active = true WHERE release_number = @number", ("number", number));
    }

    public async Task<long> DatabaseTicksAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public Task QueueWeekAsync(DateOnly week) => ExecuteAsync("""
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-draft', @week, 'Pending', 0, 0, 0, 1)
        """, ("id", BriefWeek.RecordIdOf(week)), ("week", week));

    public async Task<string> BriefQueueStatusAsync(DateOnly week)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status || ':' || attempts FROM brief_analysis WHERE week_start = @week", connection);
        command.Parameters.AddWithValue("week", week);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    public static PublishedRevisionBinding Binding(PublishedRecordKind kind, string id, int revision) => new(kind, id, revision);

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => RunAsync(connectionString, sql, parameters);

    public static async Task RunAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
