using CivicLens.Application.Reports;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Publication;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Reports;

public sealed partial class ReportDetail
{
    private ReaderReportListItem? item;

    [Inject] private GetReaderReport GetReport { get; set; } = default!;

    [Parameter] public Guid ReportId { get; set; }

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new("Reader reports", ReportRoutes.List), new(item is not null ? ReportLabels.Category(item.Report.Category) : "Report")];

    private IReadOnlyList<Fact> ReportFacts =>
    [
        new("Record", PublishedRecordLabels.Kind(item!.Report.Kind)),
        new("Revision", ReportLabels.Revision(item)),
        new("Received", Moment.Full(item.Report.ReceivedAtUtc))
    ];

    private IReadOnlyList<Fact> ResolutionFacts =>
    [
        new("Outcome", ReportLabels.Status(item!.Report.Status)),
        new("Note", item.Report.Note ?? "No note"),
        new("Resolved by", item.Report.ResolvedBy ?? ""),
        new("Resolved", item.Report.ResolvedAtUtc is { } resolved ? Moment.Full(resolved) : "")
    ];

    protected override Task LoadAsync() => LoadGuardedAsync(ReadReportAsync);

    private async Task ReadReportAsync()
    {
        item = await GetReport.ExecuteAsync(actor, ReportId, RequestAborted);
        if (item is null) ShowNotFound();
    }

    protected override void ShowNotFound()
    {
        item = null;
        base.ShowNotFound();
    }
}
