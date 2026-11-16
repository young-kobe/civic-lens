using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Officials;

/// <summary>Maps one page of the officials list to the presentation record of the server page. It reads through <see cref="OfficialStore"/>, as the JSON route does.</summary>
public sealed class OfficialsPageReader(OfficialStore officials)
{
    public const int PageSize = 25;

    /// <summary>Reads the query string of the page, which also takes the cursor of the previous page. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static OfficialRequest Parse(IQueryCollection query)
    {
        var (cursor, backward) = KeysetPage.Cursor(query);
        return OfficialRequest.Parse(query) with { Cursor = OfficialCursor.Parse(cursor), Backward = backward };
    }

    public async Task<OfficialsPageData?> ReadAsync(OfficialRequest request, CancellationToken cancellationToken)
    {
        if (await officials.ListAsync(request with { Limit = PageSize }, cancellationToken) is not { } page) return null;
        var rows = page.Officials.Select(row => new OfficialListRowData(row.OfficialId, row.Name, row.Kind, row.Office, row.State,
            row.District, row.Party, row.TermEndsOn, row.RecordCount)).ToArray();
        var filter = new OfficialsFilterData(Chamber(request.Kind), request.State, request.Party, request.NamePrefix);
        return new(page.Release, page.OfficialsInRelease, rows, filter, page.NextCursor) { PreviousCursor = page.PreviousCursor };
    }

    private static string? Chamber(PublishedOfficeKind? kind) => kind switch
    {
        PublishedOfficeKind.Representative => "house",
        PublishedOfficeKind.Senator => "senate",
        PublishedOfficeKind.Executive => "executive",
        _ => null
    };
}
