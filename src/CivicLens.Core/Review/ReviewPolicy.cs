using System.Collections.Immutable;

namespace CivicLens.Core.Review;

/// <summary>Pure review decision rules shared by every record type that people approve.</summary>
public static class ReviewPolicy
{
    public const int MaximumBlockersPerCycle = 64;

    /// <summary>A cycle holds its opening approval and at most <see cref="MaximumBlockersPerCycle"/> other decisions. Only an approval resolves
    /// a concern, and the next approval opens a new cycle.</summary>
    public const int MaximumDecisionsPerCycle = MaximumBlockersPerCycle + 1;

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
            ReviewDecisionKind.Reject => ReviewStatus.Rejected,
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
            decision.ResolvedDecisionIds.IsDefault || decision.ResolvedDecisionIds.Length > MaximumBlockersPerCycle ||
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
        if ((decision.Kind == ReviewDecisionKind.Reject) != decision.ReasonCode.HasValue ||
            (decision.ReasonCode is { } reason && !Enum.IsDefined(reason)))
            throw new ArgumentException("A rejection needs a reason code, and no other decision takes one.", nameof(decision));
    }

    private static void ValidateDecisionAgainstHistory(int currentRevisionNumber, ImmutableArray<ReviewDecision> decisions,
        ReviewDecision decision)
    {
        var currentConcerns = GetUnresolvedConcerns(decisions);
        if (decision.Kind != ReviewDecisionKind.Approve && decisions.Count(item => item.Kind != ReviewDecisionKind.Approve) >= MaximumBlockersPerCycle)
            throw new ReviewCycleFullException();
        var outstanding = currentConcerns.Select(item => item.DecisionId).Order(StringComparer.Ordinal).ToArray();
        var resolved = decision.ResolvedDecisionIds.Order(StringComparer.Ordinal).ToArray();
        if (decision.Kind == ReviewDecisionKind.Approve && !outstanding.SequenceEqual(resolved, StringComparer.Ordinal))
            throw new ArgumentException("Approval must explicitly resolve every outstanding concern.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.Approve && outstanding.Length > 0 && string.IsNullOrWhiteSpace(decision.Note))
            throw new ArgumentException("Resolving concerns requires a resolution note.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.WithdrawApproval && LatestFor(decisions, currentRevisionNumber)?.Kind != ReviewDecisionKind.Approve)
            throw new ArgumentException("Approval can only be withdrawn while the current revision is approved.", nameof(decision));
        if (decision.Kind == ReviewDecisionKind.Reject &&
            LatestFor(decisions, currentRevisionNumber)?.Kind is ReviewDecisionKind.Approve or ReviewDecisionKind.Reject)
            throw new ArgumentException("Only a revision that is neither approved nor rejected can be rejected.", nameof(decision));
    }
}
