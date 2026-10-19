using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class StatementTriage
{
    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Statements", StatementRoutes.List), new("Triage")];

    private ReviewTriagePage<StatementReviewListItem>? triage;

    [Inject] private ListStatementTriage ListTriage { get; set; } = default!;
    [Inject] private DecideStatementReview DecideReview { get; set; } = default!;

    protected override string Route => StatementRoutes.Triage;

    protected override Task LoadAsync() => LoadGuardedAsync(
        async () => triage = await ListTriage.ExecuteAsync(actor, DraftsCursor, ReadListFilter(), RequestAborted),
        "The statement triage could not load. Refresh the page to try again.");

    protected override Task DecideRecordAsync(BulkApprovalTarget target, ReviewDecisionKind kind, string? note, string idempotencyKey) =>
        DecideReview.ExecuteAsync(actor, new(target.RecordId, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion, kind, note, [],
            idempotencyKey), RequestAborted);
}
