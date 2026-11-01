using CivicLens.Core.Review;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed record StoredDecision(int RevisionNumber, int ReviewStateVersion, ReviewDecisionKind Kind, string ActorSubject);

internal static class ReviewDecisionRows
{
    public static async Task<IReadOnlyList<StoredDecision>> ReadAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT revision_number, review_state_version, kind, actor_subject FROM {table} ORDER BY review_state_version", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var decisions = new List<StoredDecision>();
        while (await reader.ReadAsync())
            decisions.Add(new StoredDecision(reader.GetInt32(0), reader.GetInt32(1), Enum.Parse<ReviewDecisionKind>(reader.GetString(2)), reader.GetString(3)));
        return decisions;
    }
}
