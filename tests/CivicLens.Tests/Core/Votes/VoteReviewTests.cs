using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Votes;

public sealed class VoteReviewTests
{
    [Theory]
    [InlineData(VoteKind.Rule)]
    [InlineData(VoteKind.PreviousQuestion)]
    [InlineData(VoteKind.MotionToRecommit)]
    public void AProceduralFirstDraftIsBulkSafeEvenWithModelTextBecauseTheKindAloneNamesWhatTheReviewerChecks(VoteKind kind)
    {
        var current = Revision() with { Kind = kind, TextOrigin = ReviewTextOrigin.Model };

        Assert.True(Review(current, approved: null).IsBulkSafe);
    }

    [Theory]
    [InlineData(VoteKind.Amendment)]
    [InlineData(VoteKind.Suspension)]
    [InlineData(VoteKind.Passage)]
    [InlineData(VoteKind.ConferenceReport)]
    [InlineData(VoteKind.VetoOverride)]
    [InlineData(VoteKind.Other)]
    public void AFirstDraftThatIsNotProceduralNeedsAReviewerBecauseNothingIsApprovedToCompareWith(VoteKind kind)
    {
        Assert.False(Review(Revision() with { Kind = kind }, approved: null).IsBulkSafe);
    }

    [Fact]
    public void ARevisionThatChangesOnlyMemberVotesIsBulkSafeAfterAnApprovalOfTheSameTextAndKind()
    {
        var approved = Revision();
        var current = approved with { RevisionNumber = 2, Members = [approved.Members[0] with { Cast = "Nay" }] };

        Assert.True(Review(current, approved).IsBulkSafe);
    }

    [Fact]
    public void ARevisionWithNewTextOrANewKindIsNotBulkSafeBecauseTheApprovalDoesNotCoverIt()
    {
        var approved = Revision();

        Assert.False(Review(approved with { RevisionNumber = 2, Headline = "Other" }, approved).IsBulkSafe);
        Assert.False(Review(approved with { RevisionNumber = 2, Summary = "Other." }, approved).IsBulkSafe);
        Assert.False(Review(approved with { RevisionNumber = 2, TextOrigin = ReviewTextOrigin.Model }, approved).IsBulkSafe);
        Assert.False(Review(approved with { RevisionNumber = 2, Kind = VoteKind.Amendment }, approved).IsBulkSafe);
    }

    [Fact]
    public void AMemberOnlyCorrectionStaysBulkSafeWhenTheSourceMovedItsCitationsBecauseTheApprovalCoversTheFactsNotTheirPlace()
    {
        var approved = Revision();
        var moved = approved.Facts.Select(fact => fact with { Citation = fact.Citation with { Start = fact.Citation.Start + 1 } }).ToImmutableArray();
        var current = approved with
        {
            RevisionNumber = 2,
            CandidateId = new string('9', 64),
            CandidateUpdateDate = approved.CandidateUpdateDate.AddDays(1),
            IssueRevisionId = new string('8', 64),
            Facts = moved,
            VotedOnCitation = approved.VotedOnCitation with { Start = 1 },
            Members = [approved.Members[0] with { Cast = "Nay", Citation = approved.Members[0].Citation with { Start = 31 } }]
        };

        Assert.True(Review(current, approved).IsBulkSafe);
    }

    [Fact]
    public void AChangedFactBillOrVoteDayBlocksBulkApprovalBecauseTheApprovalDoesNotCoverIt()
    {
        var approved = Revision();
        var next = approved with { RevisionNumber = 2 };

        Assert.False(Review(next with { Facts = [approved.Facts[0] with { Text = "Started: other" }, .. approved.Facts.Skip(1)] }, approved).IsBulkSafe);
        Assert.False(Review(next with { BillNumber = 99 }, approved).IsBulkSafe);
        Assert.False(Review(next with { BillType = "s" }, approved).IsBulkSafe);
        Assert.False(Review(next with { VotedOn = approved.VotedOn.AddDays(1) }, approved).IsBulkSafe);
        Assert.False(Review(next with { RollCallNumber = 101 }, approved).IsBulkSafe);
        Assert.False(Review(next with { TrackedIssueIds = ["other"] }, approved).IsBulkSafe);
    }

    [Fact]
    public void AnOpenConcernBlocksEveryClassBecauseBulkApprovalWritesNoResolutionNote()
    {
        var approved = Revision();
        var concern = new ReviewDecision(new string('a', 32), approved.RecordId, 2, 2, ReviewDecisionKind.RequestChanges, "auth0|reviewer",
            "Check it.", [], DateTimeOffset.UnixEpoch);
        var procedural = Revision() with { Kind = VoteKind.Rule };

        Assert.False(Review(approved with { RevisionNumber = 2 }, approved, concern).IsBulkSafe);
        Assert.False(Review(procedural, null, concern).IsBulkSafe);
    }

    private static VoteDraftRevision Revision() => RelationshipFixtures.Vote();

    private static VoteReview Review(VoteDraftRevision current, VoteDraftRevision? approved, params ReviewDecision[] concerns) =>
        new(current.RecordId, current, approved, [current], 0, ReviewStatus.AwaitingReview, [], [.. concerns]);
}
