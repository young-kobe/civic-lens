using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal static class StoredRunInputs
{
    /// <summary>Asserts that the runs of one subject, and of one task when given, stored the exact requests, oldest run first, and that each hash matches its stored parts.</summary>
    public static async Task AssertMatchAsync(string connectionString, string runTable, string subjectColumn, string subjectId,
        IReadOnlyList<AnalysisModelRequest> requests, string? task = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT r.user_message, s.text, o.text, r.effort, r.max_output_tokens, r.input_hash
              FROM {runTable} r
              JOIN analysis_system_prompts s ON s.text_sha256 = r.system_prompt_sha256
              JOIN analysis_output_schemas o ON o.text_sha256 = r.output_schema_sha256
             WHERE r.{subjectColumn} = @subject AND (CAST(@task AS text) IS NULL OR r.task = @task) ORDER BY r.started_at_utc_ticks, r.run_id
            """, connection);
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("task", (object?)task ?? DBNull.Value);
        var stored = new List<(string User, string System, string Schema, string Effort, int MaximumTokens, string Hash)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                stored.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5)));

        Assert.NotEmpty(requests);
        Assert.Equal(requests.Count, stored.Count);
        foreach (var (request, row) in requests.Zip(stored))
        {
            Assert.Equal((request.UserPrompt, request.SystemPrompt, request.SchemaJson, request.Effort, request.MaximumOutputTokens, request.InputHash),
                (row.User, row.System, row.Schema, row.Effort, row.MaximumTokens, row.Hash));
            Assert.Equal(row.Hash, AnalysisPromptHash.Compute(request.Model, row.Effort, row.MaximumTokens, row.System, row.User, row.Schema));
        }
        Assert.Equal(stored.Count, stored.Select(row => row.User).Distinct().Count());
    }

    /// <summary>Reads the input that one run stored, or null when the run stored none.</summary>
    public static async Task<AnalysisRunInput?> ReadAsync(string connectionString, string runTable, string runId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT r.system_prompt_sha256 IS NOT NULL, s.text, r.user_message, o.text, r.effort, r.max_output_tokens
              FROM {runTable} r
              LEFT JOIN analysis_system_prompts s ON s.text_sha256 = r.system_prompt_sha256
              LEFT JOIN analysis_output_schemas o ON o.text_sha256 = r.output_schema_sha256
             WHERE r.run_id = @run
            """, connection);
        command.Parameters.AddWithValue("run", runId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.GetBoolean(0)
            ? new AnalysisRunInput(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5))
            : null;
    }
}
