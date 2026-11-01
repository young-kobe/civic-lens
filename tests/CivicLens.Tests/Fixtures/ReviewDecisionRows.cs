using System.Collections.Immutable;
using CivicLens.Core.Review;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal static class ReviewDecisionRows
{
    public static async Task<IReadOnlyList<ReviewDecision>> ReadAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT decision_id, record_id, revision_number, review_state_version, kind, actor_subject, note, correction_note, created_at_utc_ticks
            FROM {table} ORDER BY review_state_version
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var decisions = new List<ReviewDecision>();
        while (await reader.ReadAsync())
            decisions.Add(new ReviewDecision(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
                Enum.Parse<ReviewDecisionKind>(reader.GetString(4)), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                ImmutableArray<string>.Empty, new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return decisions;
    }
}
