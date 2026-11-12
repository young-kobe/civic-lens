using System.Data;
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

    /// <summary>Reads the page. Null means that no release is active or that the active release names no such official.</summary>
    public async Task<OfficialRecordPageData?> ReadAsync(string officialId, OfficialRecordRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await OfficialStore.ReadOfficialAsync(connection, transaction, officialId, cancellationToken) is not var (release, _, official)) return null;
        var counts = await OfficialRecordQueries.ReadCountsAsync(connection, transaction, officialId, cancellationToken);
        var tabs = TabOrder.Where(kind => counts[kind] > 0).Select(kind => new OfficialRecordCountData(kind, counts[kind])).ToArray();
        var selected = request.Kind is { } asked && counts[asked] > 0 ? asked : tabs.FirstOrDefault()?.Kind ?? OfficialRecordKind.Votes;
        var page = new OfficialRecordPageData(release, official.OfficialId, official.Name, official.Kind, tabs, selected)
        {
            Offices = await ReadOfficesAsync(connection, transaction, officialId, cancellationToken)
        };
        return tabs.Length == 0 ? page : await ReadPageAsync(connection, transaction, page, request.Kind == selected ? request.Cursor : null, cancellationToken);
    }

    private static async Task<OfficialOfficeData[]> ReadOfficesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        var terms = await OfficialStore.ReadTermsAsync(connection, transaction, officialId, cancellationToken);
        return [.. terms.OrderBy(term => term.StartsOn)
            .Select(term => new OfficialOfficeData(OfficialSeat.Text(term.Kind, term.Office, term.State, term.District) ?? term.Office, term.Party, term.StartsOn, term.EndsOn))];
    }

    private static async Task<OfficialRecordPageData> ReadPageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfficialRecordPageData page,
        OfficialRecordCursor? after, CancellationToken cancellationToken)
    {
        var id = page.OfficialId;
        switch (page.Selected)
        {
            case OfficialRecordKind.Votes:
                var votes = await OfficialRecordQueries.ReadVotesAsync(connection, transaction, id, after, cancellationToken);
                return Continue(page with { Votes = votes.Rows }, votes.Next);
            case OfficialRecordKind.Bills:
                var bills = await OfficialRecordQueries.ReadBillsAsync(connection, transaction, id, after, cancellationToken);
                return Continue(page with { Bills = bills.Rows }, bills.Next);
            case OfficialRecordKind.Statements:
                var statements = await OfficialRecordQueries.ReadStatementsAsync(connection, transaction, id, after, cancellationToken);
                return Continue(page with { Statements = statements.Rows }, statements.Next);
            default:
                var actions = await OfficialRecordQueries.ReadExecutiveActionsAsync(connection, transaction, id, after, cancellationToken);
                return Continue(page with { ExecutiveActions = actions.Rows }, actions.Next);
        }
    }

    private static OfficialRecordPageData Continue(OfficialRecordPageData page, (DateOnly Day, string RecordId)? next) =>
        next is { } key ? page with { NextCursor = new OfficialRecordCursor(page.Release, key.Day, key.RecordId).Encode() } : page;
}
