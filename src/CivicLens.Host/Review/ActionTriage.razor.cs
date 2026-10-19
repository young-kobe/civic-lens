using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class ActionTriage
{
    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Actions", ActionRoutes.List), new("Triage")];

    private ReviewTriagePage<ActionReview>? triage;

    [Inject] private ListActionTriage ListTriage { get; set; } = default!;
    [Inject] private DecideActionReview DecideReview { get; set; } = default!;
    [Inject] private ApproveSponsorshipChanges Approve { get; set; } = default!;

    protected override string Route => ActionRoutes.Triage;

    protected override Task LoadAsync() => LoadGuardedAsync(
        async () => triage = await ListTriage.ExecuteAsync(actor, DraftsCursor, ReadListFilter(), RequestAborted),
        "The action triage could not load. Refresh the page to try again.");

    protected override Task DecideRecordAsync(BulkApprovalTarget target, ReviewDecisionKind kind, string? note, string idempotencyKey) =>
        DecideReview.ExecuteAsync(actor, new(target.RecordId, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion, kind, note, [],
            idempotencyKey), RequestAborted);

    protected override Task<ImmutableArray<BulkApprovalResult>> ApproveBulkAsync(BulkApprovalRequest request) =>
        Approve.ExecuteAsync(actor, request, RequestAborted);

    private IEnumerable<(string Label, SourceSpan Span)> QuotesOf(ActionDraftRevision revision)
    {
        foreach (var sponsorship in revision.Sponsorships)
            yield return ($"Sponsorship by {OfficialName(sponsorship.PersonId)}", sponsorship.Citations[0]);
        if (revision.PolicyAreaCitation is { } area) yield return ("Policy area", area);
        if (revision.SummaryCitation is { } summary) yield return ("CRS summary", summary);
    }

    private string OfficialName(string personId) => OfficialChoices.FirstOrDefault(person => person.Key == personId).Value ?? personId;
}
