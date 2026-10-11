using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class DocumentChangeReviewPolicyTests
{
    [Fact]
    public void RequestChangesCarriesAcrossRevisionAndApprovalMustResolveExactConcern()
    {
        var draftId = new string('a', 32);
        var revision = Revision(draftId, 1);
        var review = Review(draftId, revision, 0, []);
        var concern = Decision(draftId, 1, 1, ReviewDecisionKind.RequestChanges, "Add context.");
        DocumentChangeReviewPolicy.ValidateDecision(review, concern);

        var nextRevision = Revision(draftId, 2);
        var carried = Review(draftId, nextRevision, 1, [concern]);
        Assert.Same(concern, Assert.Single(DocumentChangeReviewPolicy.GetUnresolvedConcerns(carried.Decisions)));
        var incompleteApproval = Decision(draftId, 2, 2, ReviewDecisionKind.Approve, null);
        Assert.Throws<ArgumentException>(() => DocumentChangeReviewPolicy.ValidateDecision(carried, incompleteApproval));

        var approval = Decision(draftId, 2, 2, ReviewDecisionKind.Approve, "Resolved with the new context.", [concern.DecisionId]);
        DocumentChangeReviewPolicy.ValidateDecision(carried, approval);
        Assert.Empty(DocumentChangeReviewPolicy.GetUnresolvedConcerns([concern, approval]));
    }

    [Fact]
    public void WithdrawalRequiresCurrentApprovalAndCreatesAConcernForReapproval()
    {
        var draftId = new string('b', 32);
        var revision = Revision(draftId, 1);
        var review = Review(draftId, revision, 0, []);
        var approve = Decision(draftId, 1, 1, ReviewDecisionKind.Approve, null);
        DocumentChangeReviewPolicy.ValidateDecision(review, approve);
        var approved = Review(draftId, revision, 1, [approve]);
        var withdrawal = Decision(draftId, 1, 2, ReviewDecisionKind.WithdrawApproval, "Attribution needs review.");
        DocumentChangeReviewPolicy.ValidateDecision(approved, withdrawal);
        var withdrawn = Review(draftId, revision, 2, [approve, withdrawal]);
        Assert.Equal(withdrawal, Assert.Single(DocumentChangeReviewPolicy.GetUnresolvedConcerns(withdrawn.Decisions)));
        Assert.Throws<ArgumentException>(() => DocumentChangeReviewPolicy.ValidateDecision(withdrawn,
            Decision(draftId, 1, 3, ReviewDecisionKind.WithdrawApproval, "Withdraw again.")));
    }

    [Theory]
    [InlineData(ReviewDecisionKind.Approve, null)]
    [InlineData(ReviewDecisionKind.RequestChanges, "Needs context.")]
    public void TheAiDrafterCannotRecordAnyDecisionEvenOnItsOwnDraft(ReviewDecisionKind kind, string? note)
    {
        var draftId = new string('d', 32);
        var revision = Revision(draftId, 1) with { AuthorSubject = ReviewAuthor.AnalysisSubject };
        var review = Review(draftId, revision, 0, []);
        var decision = Decision(draftId, 1, 1, kind, note) with { ActorSubject = ReviewAuthor.AnalysisSubject };
        Assert.Throws<ArgumentException>(() => DocumentChangeReviewPolicy.ValidateDecision(review, decision));

        DocumentChangeReviewPolicy.ValidateDecision(review, decision with { ActorSubject = "auth0|owner" });
    }

    [Fact]
    public void AnAiProposedChangeDateNeedsAHumanRevisionBeforeApproval()
    {
        var citation = new DocumentChangeCitation(new string('e', 64), 0, 4);
        var dated = Revision(new string('d', 32), 1) with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            ChangeDate = new DateOnly(2026, 10, 3),
            ChangeDateEvidence = citation,
            Citations = [citation]
        };

        Assert.Throws<ArgumentException>(dated.ValidateForApproval);
        (dated with { RevisionNumber = 2, AuthorSubject = "auth0|owner" }).ValidateForApproval();
        (dated with { ChangeDate = null, ChangeDateEvidence = null }).ValidateForApproval();
    }

    private static DocumentChangeDraftRevision Revision(string draftId, int number) =>
        new(draftId, number, new string('c', 64), "reviewer", DateTimeOffset.UnixEpoch,
            "Headline", "Summary", null, null, "Institution", null, null, [], [], []);

    private static DocumentChangeReview Review(string draftId, DocumentChangeDraftRevision revision,
        int version, ImmutableArray<ReviewDecision> decisions) =>
        new(draftId, revision, version, [revision], decisions,
            DocumentChangeReviewPolicy.GetUnresolvedConcerns(decisions));

    private static ReviewDecision Decision(string draftId, int revision, int state,
        ReviewDecisionKind kind, string? note, ImmutableArray<string> resolved = default) =>
        new(Guid.NewGuid().ToString("N"), draftId, revision, state, kind, "reviewer", note,
            resolved.IsDefault ? [] : resolved, DateTimeOffset.UnixEpoch);
}
