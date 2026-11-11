using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Search;

/// <summary>Reads the issue names of the search filter from <c>public_api.release_issues</c>, the view that <c>ZipDistrictStore</c> reads for its issue options.</summary>
public sealed class SearchPageStore(NpgsqlDataSource views)
{
    private const string IssuesSql = "SELECT issue_id, name FROM public_api.release_issues ORDER BY name, issue_id";

    public async Task<SearchChoiceData[]> ListIssuesAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(IssuesSql);
        var issues = new List<SearchChoiceData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) issues.Add(new(reader.GetString(0), reader.GetString(1)));
        return [.. issues];
    }
}
