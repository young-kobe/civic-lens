using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Infrastructure.Analysis;

/// <summary>Owns the insert of prompt text. Runs with the same system prompt or output schema share one row.</summary>
public static class AnalysisPromptTexts
{
    private static readonly string InsertSystemPromptSql = InsertSqlFor("analysis_system_prompts");
    private static readonly string InsertOutputSchemaSql = InsertSqlFor("analysis_output_schemas");

    /// <summary>Inserts the system prompt first and the output schema second, so every writer takes the locks in one order. Call it in the settle transaction.</summary>
    public static async Task EnsureAsync(CollectionAttemptDbContext db, AnalysisRunInput input, long nowTicks, CancellationToken cancellationToken)
    {
        _ = await InsertAsync(db, InsertSystemPromptSql, input.SystemPromptSha256, input.SystemPrompt, nowTicks, cancellationToken);
        _ = await InsertAsync(db, InsertOutputSchemaSql, input.OutputSchemaSha256, input.OutputSchema, nowTicks, cancellationToken);
    }

    private static Task<int> InsertAsync(CollectionAttemptDbContext db, string sql, string hash, string text, long nowTicks,
        CancellationToken cancellationToken) => db.Database.ExecuteSqlRawAsync(sql,
    [
        new NpgsqlParameter("text_sha256", hash),
        new NpgsqlParameter("text", text),
        new NpgsqlParameter("created_at_utc_ticks", nowTicks)
    ], cancellationToken);

    private static string InsertSqlFor(string table) =>
        $"INSERT INTO {table} (text_sha256, text, created_at_utc_ticks) VALUES (@text_sha256, @text, @created_at_utc_ticks) ON CONFLICT DO NOTHING";
}
