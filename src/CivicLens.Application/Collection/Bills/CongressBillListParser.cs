using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillListParser
{
    public const string Version = "congress-bill-list-v1";
    private const string ApiKeyParameter = "api_key";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static CongressBillListPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, BillStreamWindow window)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var wire = Deserialize(json);
        if (wire.Bills is null || wire.Pagination is null)
            throw new InvalidDataException("Bill list page must contain bills and pagination.");
        if (wire.Bills.Length > ApiPageQuery.PageLimit)
            throw new InvalidDataException("Bill list page holds more bills than the request limit.");
        var items = wire.Bills.Select(ToItem).ToArray();
        var bills = items.OfType<CongressBillListItem>().ToArray();
        return new CongressBillListPage(bills, ReadNext(wire.Pagination.Next, scope, window), BillStreamWindow.StartsPass(scope.Url),
            items.Length - bills.Length);
    }

    private static WirePage Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<WirePage>(json, Options)
                ?? throw new InvalidDataException("Bill list page is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Bill list page is not valid JSON of the expected shape.", exception);
        }
    }

    private static CongressBillListItem? ToItem(WireBill? bill)
    {
        if (bill is not { Congress: >= 1, Number: >= 1, UpdateDate: not null, Type: not null })
            throw new InvalidDataException("Bill list item is incomplete.");
        return CongressBillListItem.NormalizeType(bill.Type) is { } type
            ? new CongressBillListItem(bill.Congress.Value, type, bill.Number.Value, bill.UpdateDate.Value)
            : null;
    }

    private static string? ReadNext(string? next, CollectionRequest scope, BillStreamWindow window)
    {
        if (string.IsNullOrEmpty(next)) return null;
        if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) || !scope.Allows(uri))
            throw new InvalidDataException("Next page link is outside the source scope.");
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query.AllKeys.Any(key => string.Equals(key, ApiKeyParameter, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Next page link carries a credential.");
        if (!window.Matches(next))
            throw new InvalidDataException("Next page link leaves the requested window.");
        return next;
    }

    private sealed record WirePage(WireBill?[]? Bills, WirePagination? Pagination);
    private sealed record WireBill(int? Congress, string? Type, int? Number, DateOnly? UpdateDate);
    private sealed record WirePagination(string? Next);
}
