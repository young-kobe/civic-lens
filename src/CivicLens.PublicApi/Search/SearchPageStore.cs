using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Search;

/// <summary>Reads the two inputs of the search page that the search index does not hold: officials whose name matches the query and the issue names of the filter.
/// Both read <c>public_api</c> views, as <c>ZipDistrictStore</c> reads its issue options.</summary>
public sealed class SearchPageStore(NpgsqlDataSource views)
{
    /// <summary>The most officials that the page shows above the results.</summary>
    public const int MaximumOfficials = 3;

    /// <summary>A shorter query would match too many names.</summary>
    public const int MinimumOfficialText = 3;

    private const string OfficialsSql = """
        SELECT official_id, name, kind, state, district, party
          FROM public_api.officials
         WHERE starts_with(lower(name), @text) OR strpos(lower(name), ' ' || @text) > 0
         ORDER BY (lower(name) = @text) DESC, name, official_id
         LIMIT @take
        """;

    private const string IssuesSql = "SELECT issue_id, name FROM public_api.release_issues ORDER BY name, issue_id";

    /// <summary>Finds officials whose name, or any word of it, starts with the text. The text is the lower case query.</summary>
    public async Task<SearchOfficialData[]> FindOfficialsAsync(string lowerText, CancellationToken cancellationToken)
    {
        if (lowerText.Length < MinimumOfficialText) return [];
        await using var command = views.CreateCommand(OfficialsSql);
        command.Parameters.AddWithValue("text", lowerText);
        command.Parameters.AddWithValue("take", MaximumOfficials);
        var found = new List<SearchOfficialData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            found.Add(new(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : ViewColumns.Enum<PublishedOfficeKind>(reader, 2),
                ViewColumns.Text(reader, 3), ViewColumns.Number(reader, 4), ViewColumns.Text(reader, 5)));
        return [.. found];
    }

    public async Task<SearchChoiceData[]> ListIssuesAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(IssuesSql);
        var issues = new List<SearchChoiceData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) issues.Add(new(reader.GetString(0), reader.GetString(1)));
        return [.. issues];
    }
}
