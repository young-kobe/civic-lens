using Npgsql;

namespace CivicLens.Tests.Fixtures;

/// <summary>Writes person rows straight into the tables, as a roster snapshot or an owner command would.</summary>
internal static class PersonSeed
{
    public static async Task InsertAsync(string connectionString, string personId, string? bioguideId = null, string? wikidataId = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO people (person_id, bioguide_id, wikidata_id, created_at_utc_ticks, created_by_actor)
            VALUES (@id, @bioguide, @wikidata, 1, 'seed')
            """, connection);
        command.Parameters.AddWithValue("id", personId);
        command.Parameters.AddWithValue("bioguide", (object?)bioguideId ?? DBNull.Value);
        command.Parameters.AddWithValue("wikidata", (object?)wikidataId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Adds the first office revision of a person with one executive term.</summary>
    public static async Task InsertOfficeAsync(string connectionString, string personId, string title)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO person_office_revisions (person_id, revision_number, note, actor_subject, recorded_at_utc_ticks)
            VALUES (@id, 1, 'seed', 'seed', 1);
            INSERT INTO person_office_revision_terms (person_id, revision_number, position, title, kind, starts_on)
            VALUES (@id, 1, 0, @title, 'Executive', '2025-01-20')
            """, connection);
        command.Parameters.AddWithValue("id", personId);
        command.Parameters.AddWithValue("title", title);
        await command.ExecuteNonQueryAsync();
    }
}
