using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

public sealed record ActionHistoryRequest
{
    public required string RecordId { get; init; }
    public ActionHistoryCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    public static ActionHistoryRequest Parse(string recordId, IQueryCollection query) => new()
    {
        RecordId = recordId,
        Cursor = ActionHistoryCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit
    };
}
