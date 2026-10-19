using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Application.Review;
using CivicLens.Core.Votes;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class VoteTriage
{
    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Votes", VoteRoutes.List), new("Triage")];

    private ReviewTriagePage<VoteReview>? triage;

    [Inject] private ListVoteTriage ListTriage { get; set; } = default!;
    [Inject] private DecideVoteReview DecideReview { get; set; } = default!;
    [Inject] private ApproveBulkSafeVotes Approve { get; set; } = default!;

    protected override string Route => VoteRoutes.Triage;

    protected override Task LoadAsync() => LoadGuardedAsync(
        async () => triage = await ListTriage.ExecuteAsync(actor, DraftsCursor, ReadListFilter(), RequestAborted),
        "The vote triage could not load. Refresh the page to try again.");

    protected override Task DecideRecordAsync(BulkApprovalTarget target, ReviewDecisionKind kind, string? note, string idempotencyKey) =>
        DecideReview.ExecuteAsync(actor, new(target.RecordId, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion, kind, note, [],
            idempotencyKey), RequestAborted);

    protected override Task<ImmutableArray<BulkApprovalResult>> ApproveBulkAsync(BulkApprovalRequest request) =>
        Approve.ExecuteAsync(actor, request, RequestAborted);
}
