using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewDecisionHistory
{
    public static ImmutableArray<ReviewDecision> Parse(int reviewStateVersion, IEnumerable<IReviewDecisionRow> rows)
    {
        var decisionRows = rows.ToArray();
        if (decisionRows.Length > ReviewPolicy.MaximumDecisionsPerRecord)
            throw new InvalidOperationException("Stored review decision history exceeds its limit.");
        var decisions = decisionRows.Select(row =>
        {
            var decision = ReviewJson.Deserialize<ReviewDecision>(row.DecisionJson, "decision");
            if (decision.DecisionId != row.DecisionId || decision.DraftId != row.RecordId ||
                decision.RevisionNumber != row.RevisionNumber || decision.ReviewStateVersion != row.ReviewStateVersion)
                throw new InvalidOperationException("Stored decision identity is inconsistent.");
            return decision;
        }).ToImmutableArray();
        if (decisions.Length != reviewStateVersion ||
            decisions.Select((decision, index) => decision.ReviewStateVersion == index + 1).Any(valid => !valid))
            throw new InvalidOperationException("Stored review decision history is inconsistent.");
        return decisions;
    }

    public static void Validate(string recordId, int revisionCount, ImmutableArray<ReviewDecision> decisions,
        Action<int> validateApprovedRevision)
    {
        var prior = ImmutableArray<ReviewDecision>.Empty;
        foreach (var decision in decisions)
        {
            if (decision.RevisionNumber < 1 || decision.RevisionNumber > revisionCount)
                throw new InvalidOperationException("Stored decision references a missing revision.");
            ReviewPolicy.ValidateDecision(recordId, decision.RevisionNumber, prior.Length, prior, decision);
            if (decision.Kind == ReviewDecisionKind.Approve) validateApprovedRevision(decision.RevisionNumber);
            prior = prior.Add(decision);
        }
    }
}
