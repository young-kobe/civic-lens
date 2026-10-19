using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillActionsParser
{
    public const string Version = "congress-bill-actions-v1";
    public static CongressBillActionsPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = CongressApiJson.Deserialize<WirePage>(json, "Action page");
        if (wire.Actions is null || wire.Pagination is null)
            throw new InvalidDataException("Action page must contain actions and pagination.");
        if (wire.Actions.Length > ApiPageQuery.PageLimit)
            throw new InvalidDataException("Action page holds more actions than the request limit.");
        var actions = wire.Actions.Select(ToAction).ToArray();
        var next = BillPageUrl.ReadNext(wire.Pagination.Next, scope, sourceUrl, ApiResource.BillActions, expected);
        return new CongressBillActionsPage(actions, next);
    }

    private static CongressBillAction ToAction(WireAction? action)
    {
        if (action is not { ActionDate: not null, Text: not null } || string.IsNullOrWhiteSpace(action.Text))
            throw new InvalidDataException("Action is incomplete.");
        return new CongressBillAction(action.ActionDate.Value, action.ActionTime, action.Text,
            Optional(action.Type), Optional(action.ActionCode), action.SourceSystem?.Code,
            Optional(action.SourceSystem?.Name));
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record WirePage(WireAction?[]? Actions, WirePagination? Pagination);
    private sealed record WireAction(DateOnly? ActionDate, TimeOnly? ActionTime, string? Text, string? Type,
        string? ActionCode, WireSourceSystem? SourceSystem);
    private sealed record WireSourceSystem(int? Code, string? Name);
    private sealed record WirePagination(string? Next);
}
