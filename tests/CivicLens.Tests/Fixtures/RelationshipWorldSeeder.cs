using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Relationships;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

// The pair finder reads only the drafts and revisions, so the seeder relaxes the foreign keys of the rows around them.
// It writes a relationship revision through the production row builder, so the columns equal the revision.
internal sealed class RelationshipWorldSeeder(string connectionString)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task StatementAsync(ApprovedStatement statement, string reviewStatus = "Approved")
    {
        await ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES (@extraction, 'attempt', 'extraction', @text, @hash) ON CONFLICT DO NOTHING;
        INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
        VALUES (@id, 'source', 'https://example.test/' || @id, @extraction, 'attempt', 1) ON CONFLICT DO NOTHING;
        INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, official_ids)
        VALUES (@id, @number, 0, @status, 1, @officials)
        ON CONFLICT (candidate_id) DO UPDATE SET current_revision_number = @number, review_status = @status, official_ids = @officials;
        """, ("id", statement.RecordId), ("number", statement.Revision.RevisionNumber), ("status", reviewStatus),
            ("extraction", RelationshipFixtures.ExtractionId), ("text", statement.ArticleText), ("hash", new string('f', 64)),
            ("officials", statement.Revision.SpeakerOfficialIds.ToArray()));
        await StatementRevisionSeeder.AddAsync(connectionString, statement.Revision);
    }

    public async Task ActionAsync(ActionDraftRevision action, string reviewStatus = "Approved", long revisedAtTicks = 1)
    {
        await ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES (@extraction, 'attempt', 'extraction', 'action', @hash) ON CONFLICT DO NOTHING;
        INSERT INTO action_candidates (candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
            configuration_revision_id, created_at_utc_ticks, includes_summaries)
        VALUES (@candidate, @source, @congress, @type, @bill, 1, 'detail', @extraction, 1, false)
        ON CONFLICT DO NOTHING;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
                                   review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id, event_on, issue_ids, official_ids)
        VALUES (@id, @source, @congress, @type, @bill, @number, 0, @status, 1, @revised, @candidate, @eventOn, @issues, @officials)
        ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number, review_status = @status, candidate_id = @candidate,
                                              last_revised_at_utc_ticks = @revised, event_on = @eventOn, issue_ids = @issues, official_ids = @officials
        """, ("id", action.RecordId), ("source", action.SourceId), ("congress", action.Congress), ("type", action.BillType),
            ("bill", action.BillNumber), ("number", action.RevisionNumber), ("status", reviewStatus),
            ("candidate", action.CandidateId), ("revised", revisedAtTicks), ("eventOn", action.EventOn()), ("issues", action.TrackedIssueIds.ToArray()),
            ("officials", action.OfficialIds().ToArray()), ("extraction", RelationshipFixtures.ExtractionId), ("hash", new string('f', 64)));
        await ActionRevisionSeeder.AddAsync(connectionString, action);
    }

    public Task VoteAsync(VoteDraftRevision vote, string reviewStatus = "Approved", long revisedAtTicks = 1) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type,
            bill_number, detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES (@candidate, @source, @chamber, @congress, @session, @roll, 1, @type, @bill, 'detail', 'members', @extraction, @extraction, @extraction, 1)
        ON CONFLICT DO NOTHING;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                                 review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id, event_on, issue_ids, official_ids)
        VALUES (@id, @source, @chamber, @congress, @session, @roll, @number, 0, @status, 1, @revised, @candidate, @eventOn, @issues, @officials)
        ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number, review_status = @status, candidate_id = @candidate,
                                              last_revised_at_utc_ticks = @revised, event_on = @eventOn, issue_ids = @issues, official_ids = @officials;
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, created_at_utc_ticks, {VoteRevisionColumnsSeed.RequiredColumns})
        VALUES (@id, @number, 'auth0|editor', 'Person', NULL, 1, {VoteRevisionColumnsSeed.PlaceholderValues})
        ON CONFLICT (record_id, revision_number) DO NOTHING
        """ + VoteRevisionColumnsSeed.Sql, [.. VoteRevisionColumnsSeed.Parameters(vote), ("id", vote.RecordId), ("candidate", vote.CandidateId), ("revised", revisedAtTicks), ("source", vote.SourceId), ("chamber", vote.Chamber.ToString()),
        ("congress", vote.Congress),
        ("session", vote.SessionNumber), ("roll", vote.RollCallNumber), ("type", vote.BillType!), ("bill", vote.BillNumber!.Value),
        ("extraction", RelationshipFixtures.ExtractionId), ("number", vote.RevisionNumber), ("status", reviewStatus),
        ("eventOn", vote.VotedOn), ("issues", vote.TrackedIssueIds.ToArray()),
        ("officials", vote.OfficialIds().ToArray())]);

    public async Task RevisionAsync(RelationshipRevision revision)
    {
        await ExecuteAsync("""
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
                created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, @number, 0, 'AwaitingReview', false, 1, 1)
            ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number
            """, ("id", revision.RecordId), ("number", revision.RevisionNumber));
        var runId = ReviewAuthor.KindOf(revision.AuthorSubject) == ReviewAuthorKind.Analysis ? RunIdOf(revision) : null;
        var row = typeof(PostgresRelationshipReviewStore).Assembly.GetType("CivicLens.Infrastructure.Relationships.RelationshipDraftRows")!
            .GetMethod("NewRevision")!.Invoke(null, [revision, runId])!;
        await using var db = await AnalysisDbContextFactory.Create(connectionString).CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        db.Add(row);
        await db.SaveChangesAsync();
    }

    private static string RunIdOf(RelationshipRevision revision) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(revision.RecordId + revision.RevisionNumber)));

    public Task SetStatusAsync(string table, string idColumn, string id, string status) =>
        ExecuteAsync($"UPDATE {table} SET review_status = @status WHERE {idColumn} = @id", ("id", id), ("status", status));

    public Task SettledRowAsync(RelationshipPair pair, string status, string? errorCode, string? fingerprint, string reviewStatus = "AwaitingReview") =>
        ExecuteAsync("""
        SET session_replication_role = replica;
        UPDATE relationship_analysis SET status = @status, error_code = @code WHERE pair_id = @id;
        INSERT INTO relationship_analysis_runs (run_id, pair_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
            input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, context_json, started_at_utc_ticks, finished_at_utc_ticks)
        VALUES (substr(@id, 1, 32), @id, 'relationship-draft', 'v', 'm', 'p', 's', @id, 'Drafted', 0, 0, 0, 0, 0, @context, 1, 1);
        INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
            created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, 1, 0, @review, false, 1, 1) ON CONFLICT DO NOTHING
        """, ("id", pair.PairId), ("status", status), ("code", (object?)errorCode ?? DBNull.Value), ("review", reviewStatus),
        ("context", fingerprint is null ? """{"pairId":"x"}""" : JsonSerializer.Serialize(new { fingerprint })));

    public async Task<List<string[]>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "null" : reader.GetValue(index).ToString()!)]);
        return rows;
    }

    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => RunAsync(connectionString, sql, parameters);

    private static async Task RunAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
