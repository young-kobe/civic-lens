using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>Maps one page of the document change list to the presentation record of the server page. It reads through <see cref="DocumentChangeStore"/> and <see cref="IssueStore"/>.</summary>
public sealed class DocumentChangeListReader(DocumentChangeStore store, IssueStore issues)
{
    public const int PageSize = 25;

    public static DocumentChangeListRequest Parse(IQueryCollection query) => DocumentChangeListRequest.Parse(query, PageSize);

    public async Task<DocumentChangeListPageData?> ReadAsync(DocumentChangeListRequest request, CancellationToken cancellationToken)
    {
        if (await store.ListAsync(request, cancellationToken) is not { } page) return null;
        var institutions = (await store.ListInstitutionsAsync(cancellationToken)).Select(name => new SearchChoiceData(name, name)).ToArray();
        var issueChoices = (await issues.ListOptionsAsync(cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Changes.Select(change => new DocumentChangeListRowData(change.RecordId, change.Headline, change.Label, change.Institution,
            change.ChangeDate, change.PlaceCount)).ToArray();
        var filter = RecordListFilter.Of(("institution", request.Institution), ("issue", request.Issue),
            ("from", request.From?.ToString("yyyy-MM-dd")), ("to", request.To?.ToString("yyyy-MM-dd")));
        return new(page.Release, rows, filter, page.NextCursor, institutions, issueChoices);
    }
}
