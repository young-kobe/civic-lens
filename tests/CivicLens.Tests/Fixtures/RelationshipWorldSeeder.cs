using System.Text.Json;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

// The pair finder reads only the drafts and revisions, so the seeder relaxes the foreign keys of the rows around them.
internal sealed class RelationshipWorldSeeder(string connectionString)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public Task StatementAsync(ApprovedStatement statement, string reviewStatus = "Approved") => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES (@extraction, 'attempt', 'extraction', @text, @hash) ON CONFLICT DO NOTHING;
        INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
        VALUES (@id, 'source', 'https://example.test/' || @id, @extraction, 'attempt', 1) ON CONFLICT DO NOTHING;
        INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
        VALUES (@id, @number, 0, @status, 1)
        ON CONFLICT (candidate_id) DO UPDATE SET current_revision_number = @number, review_status = @status;
        INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, 'auth0|editor', 'Person', NULL, @json, 1)
        ON CONFLICT (candidate_id, revision_number) DO UPDATE SET revision_json = EXCLUDED.revision_json
        """, ("id", statement.RecordId), ("number", statement.Revision.RevisionNumber), ("status", reviewStatus),
        ("extraction", RelationshipFixtures.ExtractionId), ("text", statement.ArticleText), ("hash", new string('f', 64)),
        ("json", JsonSerializer.Serialize(statement.Revision, Web)));

    public Task ActionAsync(ActionDraftRevision action, string reviewStatus = "Approved") => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
                                   review_status, created_at_utc_ticks, candidate_id, event_on, issue_ids, official_ids)
        VALUES (@id, @source, @congress, @type, @bill, @number, 0, @status, 1, @candidate, @eventOn, @issues, @officials)
        ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number, review_status = @status, candidate_id = @candidate,
                                              event_on = @eventOn, issue_ids = @issues, official_ids = @officials;
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, 'auth0|editor', 'Person', NULL, @json, 1)
        ON CONFLICT (record_id, revision_number) DO UPDATE SET revision_json = EXCLUDED.revision_json
        """, ("id", action.RecordId), ("source", action.SourceId), ("congress", action.Congress), ("type", action.BillType),
        ("bill", action.BillNumber), ("number", action.RevisionNumber), ("status", reviewStatus), ("json", JsonSerializer.Serialize(action, Web)),
        ("candidate", action.CandidateId), ("eventOn", action.EventOn()), ("issues", action.TrackedIssueIds.ToArray()),
        ("officials", action.OfficialIds().ToArray()));

    public Task VoteAsync(VoteDraftRevision vote, string reviewStatus = "Approved") => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type,
            bill_number, detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES (@candidate, @source, @chamber, @congress, @session, @roll, 1, @type, @bill, 'detail', 'members', @extraction, @extraction, @extraction, 1)
        ON CONFLICT DO NOTHING;
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                                 review_status, created_at_utc_ticks, candidate_id, event_on, issue_ids, official_ids)
        VALUES (@id, @source, @chamber, @congress, @session, @roll, @number, 0, @status, 1, @candidate, @eventOn, @issues, @officials)
        ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number, review_status = @status, candidate_id = @candidate,
                                              event_on = @eventOn, issue_ids = @issues, official_ids = @officials;
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, 'auth0|editor', 'Person', NULL, @json, 1)
        ON CONFLICT (record_id, revision_number) DO UPDATE SET revision_json = EXCLUDED.revision_json
        """, ("id", vote.RecordId), ("candidate", vote.CandidateId), ("source", vote.SourceId), ("chamber", vote.Chamber.ToString()),
        ("congress", vote.Congress),
        ("session", vote.SessionNumber), ("roll", vote.RollCallNumber), ("type", vote.BillType!), ("bill", vote.BillNumber!.Value),
        ("extraction", RelationshipFixtures.ExtractionId), ("number", vote.RevisionNumber), ("status", reviewStatus),
        ("json", JsonSerializer.Serialize(vote, Web)), ("eventOn", vote.VotedOn), ("issues", vote.TrackedIssueIds.ToArray()),
        ("officials", vote.OfficialIds().ToArray()));

    public Task RevisionAsync(RelationshipRevision revision) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
            created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, @number, 0, 'AwaitingReview', false, 1, 1)
        ON CONFLICT (record_id) DO UPDATE SET current_revision_number = @number;
        INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, @number, @author, @kind, CASE WHEN @kind = 'Analysis' THEN substr(md5(@id || @number::text), 1, 32) END, @json, 1)
        """, ("id", revision.RecordId), ("number", revision.RevisionNumber), ("author", revision.AuthorSubject),
        ("kind", ReviewAuthor.KindOf(revision.AuthorSubject).ToString()), ("json", JsonSerializer.Serialize(revision, Web)));

    public Task PriorityAsync(string analysisTable, string candidateId, string task, int priority) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO {analysisTable} (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, priority)
        VALUES (@id, @task, 'Succeeded', 0, 0, 0, 1, @priority)
        ON CONFLICT (candidate_id, task) DO UPDATE SET priority = @priority
        """, ("id", candidateId), ("task", task), ("priority", (short)priority));

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
