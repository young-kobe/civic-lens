using CivicLens.Application.Publication;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.ExecutiveActions;

public sealed partial class ExecutiveActionEditor
{
    private ExecutiveActionReviewRead? detail;

    [Inject] private GetExecutiveActionReview GetReview { get; set; } = default!;
    [Inject] private DecideExecutiveActionReview DecideReview { get; set; } = default!;

    [Parameter] public string RecordId { get; set; } = string.Empty;

    private ExecutiveActionRevision Revision => detail!.Review.CurrentRevision;

    private IReadOnlyList<Crumb> Crumbs =>
        [new("Review", "/Review"), new(detail is not null ? Revision.Title : notFound ? "Executive action not found" : "Executive action")];

    protected override Task LoadAsync() => LoadGuardedAsync(ReadRecordAsync);

    private async Task ReadRecordAsync()
    {
        detail = await GetReview.ExecuteAsync(actor, RecordId, RequestAborted);
        if (detail is null) ShowNotFound();
    }

    protected override void ShowNotFound()
    {
        detail = null;
        base.ShowNotFound();
    }

    private async Task DecideAsync() => NavigateWhenSet(detail is null ? null : await RunCommandAsync(RecordDecisionAsync, DecideFailures, LoadAsync));

    private async Task<string> RecordDecisionAsync()
    {
        var request = new DecideExecutiveActionReviewRequest(RecordId, DecideExpectedRevision, DecideExpectedStateVersion,
            ParseDecisionKind(Kind), EmptyToNull(Decision?.Note), [.. Decision?.ResolvedDecisionIds ?? []], DecideKey ?? "",
            CorrectionNoteOfApproval());
        _ = await DecideReview.ExecuteAsync(actor, request, RequestAborted);
        return ExecutiveActionRoutes.Editor(RecordId, "decided");
    }

    public override PublishedRecordReference TakeOffRecord => new(PublishedRecordKind.ExecutiveAction, RecordId);

    private async Task TakeOffSiteAsync() =>
        NavigateWhenSet(detail is null ? null : await RunTakeOffSiteAsync(detail.Review.CurrentStatus, WithdrawAsync, LoadAsync));

    private Task WithdrawAsync(ReviewDecisionKind kind, string key) =>
        DecideReview.ExecuteAsync(actor, new DecideExecutiveActionReviewRequest(RecordId, TakeOffExpectedRevision, TakeOffExpectedStateVersion, kind,
            EmptyToNull(TakeOffNote), [], key), RequestAborted);

    private IReadOnlyList<Fact> FactsOf(ExecutiveActionRevision revision) =>
    [
        new("Document", $"{revision.Kind.Label()}, Federal Register document {revision.DocumentNumber}"),
        new("Signed", Moment.Iso(revision.SignedOn)),
        new("Published", Moment.Iso(revision.PublishedOn)),
        new("President", revision.PresidentName),
        .. revision.Notices.Select(notice => new Fact("Correction notice", notice.Value))
    ];

    private IReadOnlyList<ReviewHistoryEntry> History => ReviewHistoryEntry.Newest(
        detail!.Review.Revisions.Select(item => ReviewHistoryEntry.ForRevision(item.CreatedAtUtc, item.RevisionNumber, item.AuthorSubject, item.Title)),
        detail.Review.Decisions);
}
