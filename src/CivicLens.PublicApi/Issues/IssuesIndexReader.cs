using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Issues;

/// <summary>Reads the issues index in one statement: the active release and every configured issue with its counts, in the order of the issue filter.
/// The counts are the ones the home tiles show, so the view <c>release_issues</c> stays their one source.</summary>
public sealed class IssuesIndexReader(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT a.release_number, i.issue_id, i.name, i.count_congress, i.substantive_votes, i.bills
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.release_issues i ON TRUE
         ORDER BY i.name, i.issue_id
        """;

    /// <summary>Reads the index. Null means that no release is active.</summary>
    public async Task<IssuesIndexData?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var issues = new List<IssuesIndexItemData>();
        int? release = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (reader.IsDBNull(1)) continue;
            issues.Add(new(reader.GetString(1), reader.GetString(2), ViewColumns.Number(reader, 3), ViewColumns.Number(reader, 4), ViewColumns.Number(reader, 5)));
        }
        return release is { } active ? new(active, [.. issues]) : null;
    }
}
