using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

internal sealed class DocumentChangeReviewOutcomeTally(string promptVersion)
{
    private readonly Dictionary<DocumentChangeField, int> fieldEdits = Enum.GetValues<DocumentChangeField>().ToDictionary(field => field, _ => 0);
    private int drafts;
    private int approvedUnchanged;
    private int approvedEdited;
    private int changesRequested;
    private int approvalWithdrawn;
    private int awaitingReview;

    public void Add(AiDraftOutcome outcome)
    {
        drafts++;
        switch (outcome.Current.CurrentStatus)
        {
            case DocumentChangeReviewStatus.Approved:
                AddApproved(outcome);
                break;
            case DocumentChangeReviewStatus.ChangesRequested:
                changesRequested++;
                break;
            case DocumentChangeReviewStatus.ApprovalWithdrawn:
                approvalWithdrawn++;
                break;
            default:
                awaitingReview++;
                break;
        }
    }

    public DocumentChangeReviewOutcomes ToOutcomes() => new(promptVersion, drafts, approvedUnchanged, approvedEdited,
        changesRequested, approvalWithdrawn, awaitingReview, fieldEdits.ToImmutableDictionary());

    private void AddApproved(AiDraftOutcome outcome)
    {
        var changed = DocumentChangeRevisionComparison.ChangedFields(outcome.FirstRevision, outcome.Current.CurrentRevision);
        if (changed.IsEmpty) approvedUnchanged++;
        else approvedEdited++;
        foreach (var field in changed) fieldEdits[field]++;
    }
}
