using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Statements;

/// <summary>Maps one page of the statement list to the presentation record of the server page. One snapshot holds the page, the total, the speaker names, and the issue choices,
/// so the page comes from one release. The page takes the official as a typed name, so it ignores the JSON official ID parameter.</summary>
public sealed class StatementListReader(StatementStore statements, RecordSnapshot snapshot)
{
    public const int PageSize = 25;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static StatementRequest Parse(IQueryCollection query) =>
        StatementRequest.Parse(query) with { Official = null, SpeakerName = OfficialRequest.NameOf(query, "official"), Limit = PageSize };

    public async Task<StatementListPageData?> ReadAsync(StatementRequest request, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if (await statements.ListWithTotalAsync(transaction, request, cancellationToken) is not var (page, total)) return null;
        var (officials, _) = await snapshot.ReadNamesAsync(transaction, [.. page.Statements.SelectMany(statement => statement.SpeakerIds).Distinct()], [], cancellationToken);
        var names = officials.ToDictionary(official => official.OfficialId, official => official.Name);
        var issues = (await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Statements.Select(statement => new StatementListRowData(statement.RecordId, statement.Headline, statement.Label, statement.StatementDate,
            [.. statement.SpeakerIds.Select(id => names[id])])).ToArray();
        var filter = RecordListFilter.Of(request.From, request.To, ("official", request.SpeakerName), ("issue", request.Issue));
        return new(page.Release, total, rows, filter, page.NextCursor, issues);
    }
}
