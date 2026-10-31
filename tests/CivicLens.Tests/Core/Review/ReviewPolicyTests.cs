using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class ReviewPolicyTests
{
    [Fact]
    public void RequestChangesCarriesAcrossRevisionAndApprovalMustResolveExactConcern()
    {
        var draftId = new string('a', 32);
        var review = Review(draftId, 1, 0, []);
        var concern = Decision(draftId, 1, 1, ReviewDecisionKind.RequestChanges, "Add context.");
        Validate(review, concern);

        var carried = Review(draftId, 2, 1, [concern]);
        Assert.Same(concern, Assert.Single(ReviewPolicy.GetUnresolvedConcerns(carried.Decisions)));
        var incompleteApproval = Decision(draftId, 2, 2, ReviewDecisionKind.Approve, null);
        Assert.Throws<ArgumentException>(() => Validate(carried, incompleteApproval));

        var approval = Decision(draftId, 2, 2, ReviewDecisionKind.Approve, "Resolved with the new context.", [concern.DecisionId]);
        Validate(carried, approval);
        Assert.Empty(ReviewPolicy.GetUnresolvedConcerns([concern, approval]));
    }

    [Fact]
    public void WithdrawalRequiresCurrentApprovalAndCreatesAConcernForReapproval()
    {
        var draftId = new string('b', 32);
        var review = Review(draftId, 1, 0, []);
        var approve = Decision(draftId, 1, 1, ReviewDecisionKind.Approve, null);
        Validate(review, approve);
        var approved = Review(draftId, 1, 1, [approve]);
        var withdrawal = Decision(draftId, 1, 2, ReviewDecisionKind.WithdrawApproval, "Attribution needs review.");
        Validate(approved, withdrawal);
        var withdrawn = Review(draftId, 1, 2, [approve, withdrawal]);
        Assert.Equal(withdrawal, Assert.Single(ReviewPolicy.GetUnresolvedConcerns(withdrawn.Decisions)));
        Assert.Throws<ArgumentException>(() => Validate(withdrawn,
            Decision(draftId, 1, 3, ReviewDecisionKind.WithdrawApproval, "Withdraw again.")));
    }

    [Fact]
    public void ACycleHoldsItsApprovalAndAtMostTheOpenConcernCapBecauseOnlyTheNextApprovalResolvesAConcern()
    {
        var draftId = new string('e', 32);
        var cycle = ImmutableArray.Create(Decision(draftId, 1, 1, ReviewDecisionKind.Approve, null));
        while (cycle.Length < ReviewPolicy.MaximumDecisionsPerCycle)
        {
            var concern = Decision(draftId, 1, cycle.Length + 1, ReviewDecisionKind.RequestChanges, "Check again.");
            Validate(Review(draftId, 1, cycle.Length, cycle), concern);
            cycle = cycle.Add(concern);
        }

        var full = Review(draftId, 1, cycle.Length, cycle);
        Assert.Throws<ArgumentException>(() => Validate(full,
            Decision(draftId, 1, cycle.Length + 1, ReviewDecisionKind.RequestChanges, "One more.")));
        Validate(full, Decision(draftId, 1, cycle.Length + 1, ReviewDecisionKind.Approve, "Resolved.",
            [.. cycle.Skip(1).Select(item => item.DecisionId)]));
    }

    [Theory]
    [InlineData(ReviewDecisionKind.RequestChanges)]
    [InlineData(ReviewDecisionKind.WithdrawApproval)]
    public void OnlyAnApprovalResolvesAConcernBecauseAConcernThatResolvesAnotherWouldHideIt(ReviewDecisionKind kind)
    {
        var draftId = new string('f', 32);
        var approval = Decision(draftId, 1, 1, ReviewDecisionKind.Approve, null);
        var concern = Decision(draftId, 1, 2, ReviewDecisionKind.RequestChanges, "Add context.");
        var review = Review(draftId, 1, 2, [approval, concern]);

        var failure = Assert.Throws<ArgumentException>(() => Validate(review,
            Decision(draftId, 1, 3, kind, "Not an approval.", [concern.DecisionId])));

        Assert.Contains("Only approval may resolve concerns", failure.Message);
    }

    [Theory]
    [InlineData(ReviewDecisionKind.Approve, null)]
    [InlineData(ReviewDecisionKind.RequestChanges, "Needs context.")]
    public void TheAiDrafterCannotRecordAnyDecisionEvenOnItsOwnDraft(ReviewDecisionKind kind, string? note)
    {
        var draftId = new string('d', 32);
        var review = Review(draftId, 1, 0, []);
        var decision = Decision(draftId, 1, 1, kind, note) with { ActorSubject = ReviewAuthor.AnalysisSubject };
        Assert.Throws<ArgumentException>(() => Validate(review, decision));

        Validate(review, decision with { ActorSubject = "auth0|owner" });
    }

    [Fact]
    public void ARecordWithoutARevisionIsNotDraftedWhateverTheDecisions()
    {
        Assert.Equal(ReviewStatus.NotDrafted, ReviewPolicy.GetCurrentStatus(0, []));
        Assert.Equal(ReviewStatus.AwaitingReview, ReviewPolicy.GetCurrentStatus(1, []));
    }

    [Fact]
    public void StatusFollowsTheLatestDecisionOnTheCurrentRevisionAndOpenConcerns()
    {
        var id = new string('f', 32);
        var concern = Decision(id, 1, 1, ReviewDecisionKind.RequestChanges, "Add context.");
        Assert.Equal(ReviewStatus.ChangesRequested, ReviewPolicy.GetCurrentStatus(1, [concern]));
        Assert.Equal(ReviewStatus.AwaitingReviewWithConcerns, ReviewPolicy.GetCurrentStatus(2, [concern]));
        var approval = Decision(id, 2, 2, ReviewDecisionKind.Approve, "Resolved.", [concern.DecisionId]);
        Assert.Equal(ReviewStatus.Approved, ReviewPolicy.GetCurrentStatus(2, [concern, approval]));
    }

    private sealed record State(string RecordId, int RevisionNumber, int Version, ImmutableArray<ReviewDecision> Decisions);

    private static State Review(string recordId, int revisionNumber, int version, ImmutableArray<ReviewDecision> decisions) =>
        new(recordId, revisionNumber, version, decisions);

    private static void Validate(State state, ReviewDecision decision) =>
        ReviewPolicy.ValidateDecision(state.RecordId, state.RevisionNumber, state.Version, state.Decisions, decision);

    private static ReviewDecision Decision(string draftId, int revision, int state,
        ReviewDecisionKind kind, string? note, ImmutableArray<string> resolved = default) =>
        new(Guid.NewGuid().ToString("N"), draftId, revision, state, kind, "reviewer", note,
            resolved.IsDefault ? [] : resolved, DateTimeOffset.UnixEpoch);
}
