using CivicLens.PublicApi.Search;
using Npgsql;

namespace CivicLens.PublicApi.ActiveRelease;

/// <summary>Names the active release from the restricted view <c>public_api.active_release</c>.</summary>
public sealed class ActiveReleaseReader(NpgsqlDataSource views)
{
    /// <summary>Finds the active release. Null means that no release is active.</summary>
    public async Task<ReleaseInfo?> FindAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("SELECT release_number, published_at_utc_ticks FROM public_api.active_release LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt32(0), new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero))
            : null;
    }

    /// <summary>Finds the active release with the newest roll call day of each chamber, in one statement. The days come from every published roll call, as the home answer reads them.</summary>
    public async Task<ReleaseFreshness?> FindFreshnessAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("""
            SELECT a.release_number,
                   max(v.voted_on) FILTER (WHERE v.chamber = 'House'),
                   max(v.voted_on) FILTER (WHERE v.chamber = 'Senate')
              FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
              LEFT JOIN public_api.votes v ON TRUE
             GROUP BY a.release_number
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt32(0), Day(reader, 1), Day(reader, 2))
            : null;
    }

    private static DateOnly? Day(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);
}
