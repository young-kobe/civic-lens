using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

/// <summary>A review cycle starts at the latest approval, or at the first decision when no approval exists.</summary>
internal static class ReviewDecisionHistory
{
    public static ImmutableArray<ReviewDecision> Parse(int reviewStateVersion, IEnumerable<IReviewDecisionRow> rows)
    {
        var decisionRows = rows.ToArray();
        var decisions = decisionRows.Select(row =>
        {
            var decision = ReviewJson.Deserialize<ReviewDecision>(row.DecisionJson, "decision");
            if (decision.DecisionId != row.DecisionId || decision.DraftId != row.RecordId || decision.Kind != row.Kind ||
                decision.RevisionNumber != row.RevisionNumber || decision.ReviewStateVersion != row.ReviewStateVersion)
                throw new InvalidOperationException("Stored decision identity is inconsistent.");
            return decision;
        }).ToImmutableArray();
        var start = ApprovalVersionOf(decisions);
        if (decisions.Length != reviewStateVersion - Math.Max(start - 1, 0) ||
            decisions.Select((decision, index) => decision.ReviewStateVersion == Math.Max(start, 1) + index).Any(valid => !valid))
            throw new InvalidOperationException("Stored review decision history is inconsistent.");
        return decisions;
    }

    public static int ApprovalVersionOf(ImmutableArray<ReviewDecision> cycle) =>
        cycle is [{ Kind: ReviewDecisionKind.Approve } approval, ..] ? approval.ReviewStateVersion : 0;

    public static int ApprovedRevisionOf(ImmutableArray<ReviewDecision> cycle) =>
        cycle is [{ Kind: ReviewDecisionKind.Approve } approval, ..] ? approval.RevisionNumber : 0;

    public static void Validate<TRevision>(string recordId, TRevision current, TRevision? approved, ImmutableArray<ReviewDecision> cycle)
        where TRevision : class, IReviewRevision
    {
        var approvedRevision = ApprovedRevisionOf(cycle);
        if ((approved?.RevisionNumber ?? 0) != approvedRevision || current.RevisionNumber < approvedRevision)
            throw new InvalidOperationException("Stored approved revision is inconsistent.");
        approved?.ValidateForApproval();
        var prior = approvedRevision == 0 ? ImmutableArray<ReviewDecision>.Empty : cycle[..1];
        foreach (var decision in cycle[prior.Length..])
        {
            if (decision.RevisionNumber < Math.Max(approvedRevision, 1) || decision.RevisionNumber > current.RevisionNumber)
                throw new InvalidOperationException("Stored decision references a revision outside its review cycle.");
            ReviewPolicy.ValidateDecision(recordId, decision.RevisionNumber, decision.ReviewStateVersion - 1, prior, decision);
            if (decision.Kind == ReviewDecisionKind.Approve) throw new InvalidOperationException("A review cycle holds one approval.");
            prior = prior.Add(decision);
        }
    }
}
