using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Briefs;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Publication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

// The brief selection reads only releases and the exact revision rows, so the seeder relaxes the foreign keys of the drafts around them.
internal sealed class BriefWorldSeeder(string connectionString)
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Dictionary<(PublishedRecordKind Kind, string Id, int Revision), PublishedRecordEntry> entries = [];

    public static string StatementId(char c) => new(c, 64);
    public static string ActionId(char c) => new(c, 64);
    public static string DocumentChangeId(char c) => new(c, 32);

    public async Task ReleaseAsync(int number, DateTimeOffset publishedAt, bool active, params PublishedRevisionBinding[] records)
    {
        await ReleaseWithoutApprovalsAsync(number, publishedAt, active, records);
        foreach (var record in records) await DecisionAsync(record.Kind, record.RecordId, record.RevisionNumber, record.RevisionNumber, ReviewDecisionKind.Approve);
    }

    public async Task ReleaseWithEntriesAsync(int number, DateTimeOffset publishedAt, bool active,
        IReadOnlyCollection<PublishedRecordEntry> completeEntries, params PublishedRevisionBinding[] records)
    {
        if (!records.ToHashSet().SetEquals(completeEntries.Select(PublishedRevisionBinding.Of)))
            throw new ArgumentException("A complete fixture release needs the exact entry of every record.");
        await PublishedEntryFixtures.SeedEntriesAsync(connectionString, completeEntries);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        var publications = new PostgresPublicationStore(new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<CollectionAttemptDbContext>(options), TestCodeVersion.Current);
        var bindings = await publications.PrepareCorrectionHistoriesAsync(
            [.. records.Select(record => new PublicationCorrectionHistory(record, []))], CancellationToken.None);
        await ReleaseWithoutApprovalsAsync(number, publishedAt, active, records);
        await using (var db = new CollectionAttemptDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await InternalMethods.InvokeAsync("CivicLens.Infrastructure.Publication.CorrectionHistoryCopies", "BindAsync",
                db, number, records, bindings, CancellationToken.None);
            await transaction.CommitAsync();
        }
        foreach (var record in records)
            await DecisionAsync(record.Kind, record.RecordId, record.RevisionNumber, record.RevisionNumber, ReviewDecisionKind.Approve);
    }

    public Task ReleaseTrackedAsync(int number, DateTimeOffset publishedAt, bool active, params PublishedRevisionBinding[] records)
    {
        var completeEntries = records.Select(EntryOf).ToArray();
        return ReleaseWithEntriesAsync(number, publishedAt, active, completeEntries, records);
    }

    public PublishedRecordEntry EntryOf(PublishedRevisionBinding record) =>
        entries.TryGetValue((record.Kind, record.RecordId, record.RevisionNumber), out var entry)
            ? entry
            : throw new InvalidOperationException($"No entry facts were captured for {record.Kind} {record.RecordId} revision {record.RevisionNumber}.");

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
            INSERT INTO {table} (decision_id, {column}, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
            VALUES (md5(@id || '|' || @revision::text || '|' || @version::text), @id, @revision, @version, @kind, 'auth0|seed', 1)
            ON CONFLICT DO NOTHING
            """, ("id", id), ("revision", revision), ("version", version), ("kind", decision.ToString()));
    }

    public Task StatementAsync(string id, int revision, string headline, params string[] officials)
    {
        var draft = new StatementDraftRevision(id, revision, "auth0|editor", Created,
            headline, "Summary of " + headline, new DateOnly(2026, 10, 6), new SourceSpan(new string('e', 64), 0, 5), [.. officials], [], [], [], [], []);
        entries[(PublishedRecordKind.Statement, id, revision)] = new()
        {
            Kind = PublishedRecordKind.Statement,
            RecordId = id,
            RevisionNumber = revision,
            Headline = draft.Headline,
            FirstPublishedAtUtc = Created,
            Officials = [.. officials.Select(official => new PublishedOfficial { Id = official, Name = official })],
            IssueIds = [],
            PolicyAreaIds = [],
            ApprovedBy = PublishedApprover.Person,
            EventDate = draft.StatementDate
        };
        return StatementRevisionSeeder.AddAsync(connectionString, draft);
    }

    public Task DocumentChangeAsync(string id, int revision, string headline, params string[] officials)
    {
        var draft = new DocumentChangeDraftRevision(id, revision, new string('c', 64),
            "auth0|editor", Created, headline, "Summary of " + headline, null, null, "Agency", null, null, [.. officials], [], []);
        entries[(PublishedRecordKind.DocumentChange, id, revision)] = new()
        {
            Kind = PublishedRecordKind.DocumentChange,
            RecordId = id,
            RevisionNumber = revision,
            Headline = draft.Headline,
            FirstPublishedAtUtc = Created,
            Officials = [.. officials.Select(official => new PublishedOfficial { Id = official, Name = official })],
            IssueIds = [],
            PolicyAreaIds = []
        };
        return DocumentChangeRevisionSeeder.AddAsync(connectionString, draft);
    }

    public Task ActionAsync(string id, int revision, int billNumber, params string[] sponsors)
    {
        var draft = ActionRevisionFixtures.Model(revision) with
        {
            RecordId = id,
            BillNumber = billNumber,
            Headline = $"Bill {billNumber}",
            Sponsorships = [.. sponsors.Select(sponsor => ActionRevisionFixtures.Sponsorship() with { PersonId = sponsor })]
        };
        entries[(PublishedRecordKind.Action, id, revision)] = PublishedEntryFixtures.Action(id, Created, revision) with
        {
            Headline = draft.Headline,
            Officials = [.. sponsors.Select(sponsor => new PublishedOfficial { Id = sponsor, Name = sponsor })],
            BillTitle = draft.BillTitle
        };
        return ActionRevisionSeeder.AddAsync(connectionString, draft);
    }

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
        var records = new[]
        {
            Binding(PublishedRecordKind.Statement, BriefFixtures.CitedStatement, 1),
            Binding(PublishedRecordKind.Action, BriefFixtures.CitedAction, 1),
            Binding(PublishedRecordKind.DocumentChange, BriefFixtures.CitedDocumentChange, 1)
        };
        await ReleaseWithEntriesAsync(1, BriefFixtures.InWeek, active: true,
            [.. records.Select(record => entries[(record.Kind, record.RecordId, record.RevisionNumber)])], records);
    }

    public async Task ReviewableBriefAsync(BriefRevision revision, string reviewStatus = "AwaitingReview")
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
            VALUES (@id, 'brief-draft', @week, 'Succeeded', 0, 3, 0, 1);
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, @week, @number, 0, @review, @created, @created)
            """, ("id", revision.RecordId), ("week", revision.Window.WeekStart), ("number", revision.RevisionNumber),
            ("review", reviewStatus), ("created", revision.CreatedAtUtc.UtcTicks));
        await InsertRevisionAsync(revision, revision.RecordId[..32]);
    }

    public async Task AppendRevisionAsync(BriefRevision revision)
    {
        await ExecuteAsync("SET session_replication_role = replica; UPDATE brief_drafts SET current_revision_number = @number WHERE record_id = @id",
            ("id", revision.RecordId), ("number", revision.RevisionNumber));
        await InsertRevisionAsync(revision, Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(revision.RecordId + revision.RevisionNumber)))[..32]);
    }

    public Task MakeLegacyAsync() => ExecuteAsync("""
        DELETE FROM brief_revision_paragraph_citations;
        DELETE FROM brief_revision_paragraphs;
        DELETE FROM brief_revision_citations;
        UPDATE brief_revisions SET week_start = NULL, window_start_utc_ticks = NULL, window_end_utc_ticks = NULL, first_release_number = NULL,
            cutoff_release_number = NULL, fingerprint = NULL, headline = NULL, text_origin = NULL, suggestion_headline = NULL, suggestion_run_id = NULL,
            suggestion_fingerprint = NULL, coverage_considered = NULL, coverage_sent_to_model = NULL
        """);

    /// <summary>Writes the revision row with the row mapper of production, so that the columns and the child rows equal what the store writes.
    /// The foreign keys stay off, because the seeder does not seed the cited records.</summary>
    private async Task InsertRevisionAsync(BriefRevision revision, string runId)
    {
        var row = InternalMethods.Invoke("CivicLens.Infrastructure.Briefs.BriefRevisionRows", "Create", revision, runId);
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        db.Add(row);
        await db.SaveChangesAsync();
    }

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
        await using var command = new NpgsqlCommand("SELECT status || ':' || attempts FROM brief_analysis WHERE week_start = @week AND task = 'brief-draft'", connection);
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
