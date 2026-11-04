using CivicLens.PublicApi.Search;
using Npgsql;

namespace CivicLens.PublicApi.ActiveRelease;

/// <summary>Names the active release from the view <c>public_api.active_records</c>, the same view that every other route reads.</summary>
public sealed class ActiveReleaseReader(NpgsqlDataSource views)
{
    /// <summary>Finds the active release. Null means that none is active or that it holds no record.</summary>
    public async Task<ReleaseInfo?> FindAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("SELECT release_number, published_at_utc_ticks FROM public_api.active_records LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt32(0), new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero))
            : null;
    }
}
