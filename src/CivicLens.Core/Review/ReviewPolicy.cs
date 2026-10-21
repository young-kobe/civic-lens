using System.Collections.Immutable;

namespace CivicLens.Core.Review;

/// <summary>Pure review decision rules shared by every record type that people approve.</summary>
public static class ReviewPolicy
{
    public const int MaximumOpenConcerns = 64;

    /// <summary>A cycle holds its opening approval and at most <see cref="MaximumOpenConcerns"/> concerns. Only an approval resolves
    /// a concern, and the next approval opens a new cycle.</summary>
    public const int MaximumDecisionsPerCycle = MaximumOpenConcerns + 1;

    public static ReviewStatus GetInitialStatus(int currentRevisionNumber) => GetCurrentStatus(currentRevisionNumber, []);

    public static ReviewStatus GetCurrentStatus(int currentRevisionNumber, ImmutableArray<ReviewDecision> decisions) =>
        GetCurrentStatus(currentRevisionNumber, decisions, GetUnresolvedConcerns(decisions));

    public static ReviewStatus GetCurrentStatus(int currentRevisionNumber, ImmutableArray<ReviewDecision> decisions,
        ImmutableArray<ReviewDecision> unresolvedConcerns)
    {
        if (currentRevisionNumber == 0) return ReviewStatus.NotDrafted;
        var latest = LatestFor(decisions, currentRevisionNumber);
        if (latest is null)
            return unresolvedConcerns.IsEmpty ? ReviewStatus.AwaitingReview : ReviewStatus.AwaitingReviewWithConcerns;
        return latest.Kind switch
        {
            ReviewDecisionKind.Approve => ReviewStatus.Approved,
            ReviewDecisionKind.RequestChanges => ReviewStatus.ChangesRequested,
            ReviewDecisionKind.WithdrawApproval => ReviewStatus.ApprovalWithdrawn,
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

    public static void ValidateDecision(string recordId, int currentRevisionNumber, int reviewStateVersion,
        ImmutableArray<ReviewDecision> decisions, ReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.DraftId != recordId || decision.RevisionNumber != currentRevisionNumber ||
            decision.ReviewStateVersion != reviewStateVersion + 1)
            throw new ArgumentException("Decision must target the current draft revision and review state.", nameof(decision));
        ValidateDecisionFields(decision);
        ValidateDecisionAgainstHistory(currentRevisionNumber, decisions, decision);
    }

    private static ReviewDecision? LatestFor(ImmutableArray<ReviewDecision> decisions, int revisionNumber) =>
        decisions.Where(item => item.RevisionNumber == revisionNumber)
            .OrderByDescending(item => item.ReviewStateVersion).FirstOrDefault();

    private static void ValidateDecisionFields(ReviewDecision decision)
    {
        if (!LowercaseHex.Is(decision.DecisionId, 32) ||
            !ReviewAuthor.IsValidSubject(decision.ActorSubject) || !Enum.IsDefined(decision.Kind) ||
            decision.ResolvedDecisionIds.IsDefault || decision.ResolvedDecisionIds.Length > MaximumOpenConcerns ||
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
    }

    private static void ValidateDecisionAgainstHistory(int currentRevisionNumber, ImmutableArray<ReviewDecision> decisions,
        ReviewDecision decision)
    {
        var currentConcerns = GetUnresolvedConcerns(decisions);
        if ((decision.Kind is ReviewDecisionKind.RequestChanges or ReviewDecisionKind.WithdrawApproval) && currentConcerns.Length >= MaximumOpenConcerns)
            throw new ArgumentException("Resolve outstanding concerns before adding another blocker.", nameof(decision));
        var outstanding = currentConcerns.Select(item => item.DecisionId).Order(StringComparer.Ordinal).ToArray();
        var resolved = decision.ResolvedDecisionIds.Order(StringComparer.Ordinal).ToArray();
        if (decision.Kind == ReviewDecisionKind.Approve && !outstanding.SequenceEqual(resolved, StringComparer.Ordinal))
            throw new ArgumentException("Approval must explicitly resolve every outstanding concern.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.Approve && outstanding.Length > 0 && string.IsNullOrWhiteSpace(decision.Note))
            throw new ArgumentException("Resolving concerns requires a resolution note.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.WithdrawApproval && LatestFor(decisions, currentRevisionNumber)?.Kind != ReviewDecisionKind.Approve)
            throw new ArgumentException("Approval can only be withdrawn while the current revision is approved.", nameof(decision));
    }
}
