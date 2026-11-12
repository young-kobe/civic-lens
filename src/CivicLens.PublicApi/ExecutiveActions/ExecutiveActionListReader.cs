using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Maps one page of the executive action list to the presentation record of the server page. It reads through <see cref="ExecutiveActionStore"/>.</summary>
public sealed class ExecutiveActionListReader(ExecutiveActionStore store)
{
    public const int PageSize = 25;

    private static readonly SearchChoiceData[] Kinds =
        [.. Enum.GetValues<PublishedExecutiveActionKind>().Select(kind => new SearchChoiceData(QueryValues.ChoiceText(kind), PublishedRecordLabels.ExecutiveActionKind(kind)))];

    public static ExecutiveActionListRequest Parse(IQueryCollection query) => ExecutiveActionListRequest.Parse(query, PageSize);

    public async Task<ExecutiveActionListPageData?> ReadAsync(ExecutiveActionListRequest request, CancellationToken cancellationToken)
    {
        if (await store.ListAsync(request, cancellationToken) is not { } page) return null;
        var rows = page.Actions.Select(action => new ExecutiveActionListRowData(action.RecordId, action.Headline, null, action.Kind, action.SignedOn)).ToArray();
        var filter = RecordListFilter.Of(("type", request.Kind is { } kind ? QueryValues.ChoiceText(kind) : null),
            ("from", request.From?.ToString("yyyy-MM-dd")), ("to", request.To?.ToString("yyyy-MM-dd")));
        return new(page.Release, rows, filter, page.NextCursor, Kinds);
    }
}
