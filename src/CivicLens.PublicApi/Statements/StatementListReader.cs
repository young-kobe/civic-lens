using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Statements;

/// <summary>Maps one page of the statement list to the presentation record of the server page. One snapshot holds the page, the total, the speaker names,
/// the source site, the vote link and the speaker terms of each row, and the issue choices,
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
        var names = officials.ToDictionary(official => official.OfficialId, official => official.Name, StringComparer.Ordinal);
        var facts = await statements.ReadListFactsAsync(transaction, page.Statements, cancellationToken);
        var issues = (await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Statements.Select(statement => Row(statement, names, facts[statement.RecordId])).ToArray();
        var filter = RecordListFilter.Of(request.From, request.To, ("official", request.SpeakerName), ("issue", request.Issue));
        return new(page.Release, total, rows, filter, page.NextCursor, issues);
    }

    /// <summary>The page of a refused request: the choices and the total of the first page, the typed text of each field, and no rows.</summary>
    public async Task<StatementListPageData?> ReadRefusedAsync(IQueryCollection query, string? invalid, CancellationToken cancellationToken) =>
        await ReadAsync(Parse(QueryCollection.Empty), cancellationToken) is { } data
            ? data with { Statements = [], NextCursor = null, Filter = RecordListFilter.Refused(invalid, QueryValues.Typed(query, "official", "issue", "from", "to")) }
            : null;

    // One snapshot read the page and its facts, so every statement of the page has facts and every speaker has a name.
    // The party and seat are the term of the speaker on the statement date, the rule of the statement page.
    private static StatementListRowData Row(StatementRow statement, Dictionary<string, string> names, StatementListFacts facts) =>
        new(statement.RecordId, statement.Headline, statement.Label, statement.TextOrigin, statement.StatementDate,
            [.. statement.SpeakerIds.Zip(facts.SpeakerTerms, (id, term) => new StatementSpeakerData(names[id], term.Kind, term.Office, term.State, term.District, term.Party))],
            PublishedWebsite.Host(facts.SourceUrl), facts.LinkedToVote);
}
