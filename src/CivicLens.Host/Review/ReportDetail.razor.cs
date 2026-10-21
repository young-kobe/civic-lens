using CivicLens.Application.Reports;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class ReportDetail
{
    private static readonly ReviewFailureMessages ResolveFailures = new(
        "Someone resolved this report, or it changed, after you opened the page. The current state shows below.",
        "A report that needs a correction needs a note. Add the note and try again.",
        "The report could not be saved. Try again.");

    private ReaderReportListItem? item;

    [Inject] private GetReaderReport GetReport { get; set; } = default!;
    [Inject] private ResolveReaderReport Resolve { get; set; } = default!;

    [Parameter] public Guid ReportId { get; set; }

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new("Reader reports", ReportRoutes.List), new(item is not null ? ReportLabels.Category(item.Report.Category) : "Report")];

    private IReadOnlyList<Fact> ReportFacts =>
    [
        new("Record", ReportLabels.Kind(item!.Report.Kind)),
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

    protected override string? StatusText() => Status == "resolved" ? "The report is resolved." : null;

    private async Task DecideAsync() => NavigateWhenSet(await TryDecideAsync());

    private async Task<string?> TryDecideAsync() =>
        item is null ? null : await RunCommandAsync(ResolveAsync, ResolveFailures, LoadAsync);

    private async Task<string> ResolveAsync()
    {
        var status = Enum.TryParse<ReaderReportStatus>(Kind, ignoreCase: false, out var parsed) ? parsed
            : throw new ArgumentException("Choose how to resolve the report.");
        await Resolve.ExecuteAsync(actor, new(ReportId, DecideExpectedStateVersion, status, EmptyToNull(Decision?.Note)), RequestAborted);
        return ReportRoutes.Detail(ReportId, "resolved");
    }
}
