using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Publication;

public sealed class ListPublishableDocumentChangesTests
{
    [Fact]
    public async Task ReviewerCannotReadTheReadyListBecauseOnlyTheOwnerPublishes()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a')));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Reviewer));

        Assert.Equal(0, scenario.Reviews.UnpublishedReadCount);
    }

    [Fact]
    public async Task NeverPublishedDraftIsNewAndOlderPublishedDraftIsAReplacement()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a')));
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('b'), publishedRevision: 1));

        var list = await new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Owner);

        var (added, replaced) = (list.Items[0], list.Items[1]);
        Assert.False(added.IsReplacement);
        Assert.Equal("Headline a", added.Headline);
        Assert.True(replaced.IsReplacement);
        Assert.Equal(1, replaced.PublishedRevisionNumber);
        Assert.False(list.HasMore);
    }

    [Fact]
    public async Task ApprovedDraftWithAnUnresolvedConcernIsNotReadyBecausePublishWouldRefuseIt()
    {
        var scenario = new PublicationScenario();
        var clean = scenario.AddDraft('a');
        var blocked = scenario.AddDraft('b');
        scenario.Reviews.Unpublished.Add(Candidate(clean));
        scenario.Reviews.Unpublished.Add(Candidate(blocked) with
        {
            Draft = Candidate(blocked).Draft with { UnresolvedConcernCount = 1 }
        });

        var list = await new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([clean], list.Items.Select(item => item.DraftId));
    }

    [Fact]
    public async Task DraftThatIsNotApprovedIsNotReady()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a'), status: DocumentChangeReviewStatus.AwaitingReview));

        var list = await new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Owner);

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task ReadyListIsBoundedByWhatOnePublishAcceptsAndSaysWhenMoreExist()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index <= PublishDocumentChanges.MaximumDrafts; index++)
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));

        var list = await new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(PublishDocumentChanges.MaximumDrafts, list.Items.Length);
        Assert.True(list.HasMore);
        Assert.Equal(PublishDocumentChanges.MaximumDrafts + 1, scenario.Reviews.UnpublishedLimit);
    }

    // A draft the page offers must never be refused by the publish handler, and the reverse.
    [Theory]
    [InlineData(ReviewDecisionKind.Approve, true)]
    [InlineData(ReviewDecisionKind.RequestChanges, false)]
    [InlineData(ReviewDecisionKind.WithdrawApproval, false)]
    public async Task ListAndPublishAgreeOnWhichDraftsArePublishable(ReviewDecisionKind latest, bool publishable)
    {
        var scenario = new PublicationScenario();
        var draftId = scenario.AddDraft('a', decisions:
        [
            PublicationScenario.Decision(new string('a', 32), 1, ReviewDecisionKind.Approve),
            PublicationScenario.Decision(new string('a', 32), 2, latest)
        ]);
        var review = scenario.Reviews.Items[draftId].Review;
        var status = DocumentChangeReviewPolicy.GetCurrentStatus(review.CurrentRevision, review.Decisions, review.UnresolvedConcerns);
        scenario.Reviews.Unpublished.Add(new(new(draftId, review.CurrentRevision, review.ReviewStateVersion, status,
            review.UnresolvedConcerns.Length), null));

        var listed = (await new ListPublishableDocumentChanges(scenario.Reviews).ExecuteAsync(PublicationScenario.Owner)).Items.Length == 1;
        var published = await Record.ExceptionAsync(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draftId))) is null;

        Assert.Equal(publishable, listed);
        Assert.Equal(listed, published);
    }

    private static UnpublishedApprovedDraft Candidate(string draftId, int? publishedRevision = null,
        DocumentChangeReviewStatus status = DocumentChangeReviewStatus.Approved)
    {
        var item = new DocumentChangeReviewListItem(draftId, new DocumentChangeDraftRevision(draftId, 1, "c", "author",
            PublicationScenario.ApprovedAt, $"Headline {draftId[0]}", "Summary", null, null, "City Council", null, null,
            [], [], []), 1, status, 0);
        return new(item, publishedRevision);
    }
}
