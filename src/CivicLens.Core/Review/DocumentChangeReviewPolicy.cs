using System.Collections.Immutable;

namespace CivicLens.Core.Review;

/// <summary>Pure review decision rules for immutable document-change records.</summary>
public static class DocumentChangeReviewPolicy
{
    public const int MaximumRevisionsPerDraft = 64;
    public const int MaximumDecisionsPerDraft = 128;

    public static DocumentChangeReviewStatus GetCurrentStatus(DocumentChangeDraftRevision currentRevision,
        ImmutableArray<ReviewDecision> decisions, ImmutableArray<ReviewDecision> unresolvedConcerns)
    {
        var latest = decisions.Where(item => item.RevisionNumber == currentRevision.RevisionNumber)
            .OrderByDescending(item => item.ReviewStateVersion).FirstOrDefault();
        if (latest is null)
            return unresolvedConcerns.IsEmpty ? DocumentChangeReviewStatus.AwaitingReview : DocumentChangeReviewStatus.AwaitingReviewWithConcerns;
        return latest.Kind switch
        {
            ReviewDecisionKind.Approve => DocumentChangeReviewStatus.Approved,
            ReviewDecisionKind.RequestChanges => DocumentChangeReviewStatus.ChangesRequested,
            ReviewDecisionKind.WithdrawApproval => DocumentChangeReviewStatus.ApprovalWithdrawn,
            _ => throw new InvalidOperationException("Unknown review decision kind.")
        };
    }

    public static ImmutableArray<ReviewDecision> GetUnresolvedConcerns(ImmutableArray<ReviewDecision> decisions)
    {
        var resolved = decisions.Where(item => item.Kind == ReviewDecisionKind.Approve)
            .SelectMany(item => item.ResolvedDecisionIds).ToHashSet(StringComparer.Ordinal);
        return decisions.Where(item => item.Kind is ReviewDecisionKind.RequestChanges or ReviewDecisionKind.WithdrawApproval)
            .Where(item => !resolved.Contains(item.DecisionId)).ToImmutableArray();
    }

    public static void ValidateDecision(DocumentChangeReview review, ReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.DraftId != review.DraftId || decision.RevisionNumber != review.CurrentRevision.RevisionNumber ||
            decision.ReviewStateVersion != review.ReviewStateVersion + 1)
            throw new ArgumentException("Decision must target the current draft revision and review state.", nameof(decision));
        if (decision.DecisionId is not { Length: 32 } || decision.DecisionId.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            string.IsNullOrWhiteSpace(decision.ActorSubject) || decision.ActorSubject.Length > 256 || !Enum.IsDefined(decision.Kind) ||
            decision.ResolvedDecisionIds.IsDefault || decision.ResolvedDecisionIds.Length > 64 ||
            decision.ResolvedDecisionIds.Distinct(StringComparer.Ordinal).Count() != decision.ResolvedDecisionIds.Length)
            throw new ArgumentException("Review decision is invalid.", nameof(decision));
        if (ReviewAuthor.IsAnalysis(decision.ActorSubject))
            throw new ArgumentException("A model author cannot record a review decision.", nameof(decision));
        if (decision.Note is { Length: > 4_000 } || decision.Note?.Contains('\0') == true)
            throw new ArgumentException("Review note is invalid.", nameof(decision));
        if ((decision.Kind is ReviewDecisionKind.RequestChanges or ReviewDecisionKind.WithdrawApproval) && string.IsNullOrWhiteSpace(decision.Note))
            throw new ArgumentException("This decision requires a note.", nameof(decision));
        if (decision.Kind != ReviewDecisionKind.Approve && !decision.ResolvedDecisionIds.IsEmpty)
            throw new ArgumentException("Only approval may resolve concerns.", nameof(decision));
        var currentConcerns = GetUnresolvedConcerns(review.Decisions);
        if ((decision.Kind is ReviewDecisionKind.RequestChanges or ReviewDecisionKind.WithdrawApproval) && currentConcerns.Length >= 64)
            throw new ArgumentException("Resolve outstanding concerns before adding another blocker.", nameof(decision));
        var outstanding = currentConcerns.Select(item => item.DecisionId).Order(StringComparer.Ordinal).ToArray();
        var resolved = decision.ResolvedDecisionIds.Order(StringComparer.Ordinal).ToArray();
        if (decision.Kind == ReviewDecisionKind.Approve && !outstanding.SequenceEqual(resolved, StringComparer.Ordinal))
            throw new ArgumentException("Approval must explicitly resolve every outstanding concern.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.Approve && outstanding.Length > 0 && string.IsNullOrWhiteSpace(decision.Note))
            throw new ArgumentException("Resolving concerns requires a resolution note.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.WithdrawApproval)
        {
            var lastForRevision = review.Decisions.Where(item => item.RevisionNumber == decision.RevisionNumber)
                .OrderByDescending(item => item.ReviewStateVersion).FirstOrDefault();
            if (lastForRevision?.Kind != ReviewDecisionKind.Approve)
                throw new ArgumentException("Approval can only be withdrawn while the current revision is approved.", nameof(decision));
        }
    }
}
