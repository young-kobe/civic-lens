using CivicLens.Application.Reports;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class ReportList
{
    private const int PageSize = 25;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Reader reports")];

    private ReviewActor? actor;
    private ReaderReportPage? reports;
    private int openCount;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ListReaderReports ListReports { get; set; } = default!;
    [Inject] private CountOpenReaderReports CountOpen { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "reports")] private string? FilterName { get; set; }
    [SupplyParameterFromQuery(Name = "reportsCursor")] private string? Cursor { get; set; }

    private ReaderReportFilter Filter => FilterName == ReportRoutes.ResolvedFilter ? ReaderReportFilter.Resolved : ReaderReportFilter.Open;

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            reports = await ListReports.ExecuteAsync(actor, Cursor, PageSize, Filter, cancellationToken);
            openCount = await CountOpen.ExecuteAsync(actor, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception)
        {
            error = "The reader reports could not load. Refresh the page to try again.";
        }
    }

    private IReadOnlyList<FilterTab> ReportTabs =>
    [
        new("Open", Href(null, null), Filter == ReaderReportFilter.Open, openCount),
        new("Resolved", Href(ReportRoutes.ResolvedFilter, null), Filter == ReaderReportFilter.Resolved)
    ];

    private string? ReportsHref(string? cursor) =>
        cursor is null ? null : Href(Filter == ReaderReportFilter.Resolved ? ReportRoutes.ResolvedFilter : null, cursor);

    private static string Href(string? filter, string? cursor)
    {
        var query = new List<string>();
        if (filter is not null) query.Add("reports=" + filter);
        if (cursor is not null) query.Add("reportsCursor=" + Uri.EscapeDataString(cursor));
        return ReportRoutes.List + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#reports";
    }

    private (string Title, string Text) ReportsEmpty => Filter == ReaderReportFilter.Open
        ? ("No open reports", "Every report is resolved, or no reader has reported a problem yet.")
        : ("No resolved reports", "Reports that a person dismissed or marked as needing a correction show here.");
}
