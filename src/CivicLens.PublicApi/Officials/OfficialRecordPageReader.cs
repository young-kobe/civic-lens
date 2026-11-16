using System.Data;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Reads everything of the record page of one official inside one repeatable-read snapshot: the official, the offices, the count of each kind, and one page of the selected kind.
/// The official and the offices come from <see cref="OfficialStore"/>, and the counts and the pages from <see cref="OfficialRecordQueries"/>.
/// </summary>
public sealed class OfficialRecordPageReader(NpgsqlDataSource views)
{
    private static readonly OfficialRecordKind[] TabOrder = Enum.GetValues<OfficialRecordKind>();

    /// <summary>
    /// Reads the page. Null means that no release is active or that the active release names no such official.
    /// A cursor of another release reads no rows and no offices, because the endpoint sends the reader to the first page.
    /// </summary>
    public async Task<OfficialRecordPageData?> ReadAsync(string officialId, OfficialRecordRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await OfficialStore.ReadOfficialAsync(connection, transaction, officialId, cancellationToken) is not var (release, _, official)) return null;
        var counts = await OfficialRecordQueries.ReadCountsAsync(connection, transaction, officialId, cancellationToken);
        var tabs = TabOrder.Where(kind => counts[kind] > 0).Select(kind => new OfficialRecordCountData(kind, counts[kind])).ToArray();
        var selected = request.Kind is { } asked && counts[asked] > 0 ? asked : tabs.FirstOrDefault()?.Kind ?? OfficialRecordKind.Votes;
        var page = new OfficialRecordPageData(release, official.OfficialId, official.Name, official.Kind, tabs, selected);
        // A vote filter selects the votes tab. When the official has no votes, the page falls back to another tab and drops the filters,
        // so the shown tab, the form, and the next page link agree.
        if (selected == OfficialRecordKind.Votes) page = page with { VoteFilter = request.Filter };
        if (request.Cursor is { } stale && stale.Release != release) return page;
        page = page with { Offices = await ReadOfficesAsync(connection, transaction, officialId, cancellationToken) };
        var start = new OfficialRecordQueries.PageStart(release, request.Kind == selected ? request.Cursor : null, request.Kind == selected && request.Backward);
        if (tabs.Length == 0) return page;
        // Only the votes tab shows the issue choices of its filter form.
        if (selected == OfficialRecordKind.Votes)
            page = page with { Issues = [.. (await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken)).Select(issue => issue.ToChoice())] };
        return await ReadPageAsync(connection, transaction, page, request, start, cancellationToken);
    }

    private static async Task<OfficialOfficeData[]> ReadOfficesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        var terms = await OfficialStore.ReadTermsAsync(connection, transaction, officialId, cancellationToken);
        return [.. terms.OrderBy(term => term.StartsOn)
            .Select(term => new OfficialOfficeData(OfficialSeat.Text(term.Kind, term.Office, term.State, term.District) ?? term.Office, term.Party, term.StartsOn, term.EndsOn))];
    }

    private static async Task<OfficialRecordPageData> ReadPageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfficialRecordPageData page,
        OfficialRecordRequest request, OfficialRecordQueries.PageStart start, CancellationToken cancellationToken)
    {
        var id = page.OfficialId;
        switch (page.Selected)
        {
            case OfficialRecordKind.Votes:
                var votes = await OfficialRecordQueries.ReadVotesAsync(connection, transaction, id, request, start, cancellationToken);
                return Continue(page with { Votes = votes.Rows }, votes);
            case OfficialRecordKind.Bills:
                var bills = await OfficialRecordQueries.ReadBillsAsync(connection, transaction, id, start, cancellationToken);
                return Continue(page with { Bills = bills.Rows }, bills);
            case OfficialRecordKind.Statements:
                var statements = await OfficialRecordQueries.ReadStatementsAsync(connection, transaction, id, start, cancellationToken);
                return Continue(page with { Statements = statements.Rows }, statements);
            default:
                var actions = await OfficialRecordQueries.ReadExecutiveActionsAsync(connection, transaction, id, start, cancellationToken);
                return Continue(page with { ExecutiveActions = actions.Rows }, actions);
        }
    }

    private static OfficialRecordPageData Continue<T>(OfficialRecordPageData page, OfficialRecordQueries.Page<T> read) =>
        page with { NextCursor = read.Next, PreviousCursor = read.Previous };
}
