using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal static class ExecutiveActionRows
{
    internal static readonly ReviewRecords<ExecutiveActionDraftRow, ExecutiveActionRevisionRow, ExecutiveActionReviewDecisionRow, ExecutiveActionRevision> Records =
        new(PostgresAdvisoryLock.ExecutiveActionReview, "Executive action", nameof(ExecutiveActionDraftRow.RecordId), Revised);

    /// <summary>Appends the next revision of the record that the document belongs to and approves it by code in the same transaction.</summary>
    public static async Task<ExecutiveActionRevision> AppendCodeApprovedAsync(CollectionAttemptDbContext db, string sourceId, FederalRegisterEvidence evidence,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.ExecutiveActionReview, cancellationToken);
        var recordId = ExecutiveActionIdentity.RecordId(sourceId, evidence.Document.RecordDocumentNumber);
        var draft = await db.Set<ExecutiveActionDraftRow>().SingleOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        if (draft is null) db.Add(draft = NewDraft(recordId, now));
        var revision = ExecutiveActionRevisions.Create(sourceId, draft.CurrentRevisionNumber + 1, now, evidence);
        revision.ValidateShape();
        await Records.AppendCodeApprovedAsync(db, draft, revision, NewRevision(revision), await Records.ReadCycleAsync(db, draft, cancellationToken),
            () => PassesApprovalChecksAsync(db, revision, cancellationToken), NewDecisionRow);
        return revision;
    }

    private static async Task<bool> PassesApprovalChecksAsync(CollectionAttemptDbContext db, ExecutiveActionRevision revision,
        CancellationToken cancellationToken) =>
        (await ApprovalCheck.OfAsync(() => PostgresExecutiveActionReviewStore.ValidateApprovalAsync(db, revision, cancellationToken))).Passed;

    private static void Revised(ExecutiveActionDraftRow draft, ExecutiveActionRevision revision)
    {
        draft.LastRevisedAtUtcTicks = revision.CreatedAtUtc.UtcTicks;
        ListedDraftRows.Set(draft, revision.SignedOn, [], revision.OfficialIds());
    }

    private static ExecutiveActionDraftRow NewDraft(string recordId, DateTimeOffset now) => new()
    {
        RecordId = recordId,
        CreatedAtUtcTicks = now.UtcTicks
    };

    public static ExecutiveActionRevisionRow NewRevision(ExecutiveActionRevision revision) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };

    public static ExecutiveActionReviewDecisionRow NewDecisionRow(CivicLens.Core.Review.ReviewDecision decision) => new()
    {
        DecisionId = decision.DecisionId,
        RecordId = decision.DraftId,
        RevisionNumber = decision.RevisionNumber,
        ReviewStateVersion = decision.ReviewStateVersion,
        DecisionJson = ReviewJson.Serialize(decision),
        Kind = decision.Kind,
        CreatedAtUtcTicks = decision.CreatedAtUtc.UtcTicks
    };
}
