using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal static class GateLabelRuns
{
    public static async Task InsertAsync(string connectionString, string candidateId, string runId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO statement_analysis_runs (run_id, candidate_id, task, task_version, model, prompt_version, schema_version, input_hash,
                issue_revision_id, outcome, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens,
                started_at_utc_ticks, finished_at_utc_ticks)
            VALUES (@run, @candidate, 'gate', 'v', 'model', 'p', 's', '{new string('1', 64)}', '{new string('2', 64)}', 'Classified',
                0, 0, 0, 0, 0, 0, 0)
            """, connection);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("candidate", candidateId);
        await command.ExecuteNonQueryAsync();
    }
}
