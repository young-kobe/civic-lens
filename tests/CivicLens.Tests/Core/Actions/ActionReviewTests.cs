using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Actions;

public sealed class ActionReviewTests
{
    [Fact]
    public void ASponsorshipOnlyChangeIsBulkSafeUntilAConcernIsOpenBecauseBulkApprovalWritesNoResolutionNote()
    {
        var approved = RelationshipFixtures.Action();
        var current = approved with { RevisionNumber = 2, Sponsorships = [.. approved.Sponsorships, approved.Sponsorships[0] with { PersonId = "other", BioguideId = "S000033", Role = ActionSponsorshipRole.Cosponsor }], Fingerprint = new string('f', 64) };
        var concern = new ReviewDecision(new string('a', 32), approved.RecordId, 2, 2, ReviewDecisionKind.RequestChanges, "auth0|reviewer",
            "Check it.", [], DateTimeOffset.UnixEpoch);

        Assert.True(Review(current, approved).IsBulkSafe);
        Assert.False(Review(current, approved, concern).IsBulkSafe);
    }

    [Fact]
    public void AFirstDraftOrAWordingChangeIsNotBulkSafeBecauseNothingApprovedCoversIt()
    {
        var approved = RelationshipFixtures.Action();

        Assert.False(Review(approved, null).IsBulkSafe);
        Assert.False(Review(approved with { RevisionNumber = 2, Headline = "Other" }, approved).IsBulkSafe);
    }

    private static ActionReview Review(ActionDraftRevision current, ActionDraftRevision? approved, params ReviewDecision[] concerns) =>
        new(current.RecordId, current, approved, [current], 0, ReviewStatus.AwaitingReview, [], [.. concerns]);
}
