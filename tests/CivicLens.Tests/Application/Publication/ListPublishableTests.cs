using CivicLens.Application.Actions;
using CivicLens.Core.Briefs;
using CivicLens.Tests.Fixtures;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ListPublishableTests
{
    [Fact]
    public async Task ReviewerCannotReadTheReadyListBecauseOnlyTheOwnerPublishes()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a')));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Reviewer));

        Assert.Equal(0, scenario.Reviews.UnpublishedReadCount);
    }

    [Fact]
    public async Task NeverPublishedDraftIsNewAndOlderPublishedDraftIsAReplacement()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a')));
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('b'), publishedRevision: 1));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var (added, replaced) = (list.Items[0], list.Items[1]);
        Assert.False(added.IsReplacement);
        Assert.Equal("Headline a", added.Headline);
        Assert.True(replaced.IsReplacement);
        Assert.Equal(1, replaced.PublishedRevisionNumber);
        Assert.False(list.HasMore);
    }

    [Fact]
    public async Task AWithdrawnRecordThatReviewApprovedAgainIsMarkedWithdrawnSoTheOwnerDoesNotSelectItAsNew()
    {
        var scenario = new PublicationScenario();
        var withdrawn = scenario.AddAction('a');
        var fresh = scenario.AddAction('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([new(PublishedRecordKind.Action, withdrawn)], "k1"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Action, withdrawn, "Wrong.")] });
        scenario.ReviseAction(withdrawn, 2);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[withdrawn].Review, null));
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[fresh].Review, null));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([(withdrawn, true), (fresh, false)], list.Items.Select(item => (item.RecordId, item.IsWithdrawn)));
    }

    private static async Task<(PublicationScenario Scenario, ReadyToPublishList List, string Same, string Higher)> WithdrawnAtRevisionOneAsync()
    {
        var scenario = new PublicationScenario();
        var same = scenario.AddAction('a');
        var higher = scenario.AddAction('b');
        foreach (var id in new[] { same, higher })
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([new(PublishedRecordKind.Action, id)], $"k-{id}"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "w")
        {
            Withdrawals = [new(PublishedRecordKind.Action, same, "Wrong."), new(PublishedRecordKind.Action, higher, "Wrong.")]
        });
        scenario.ReviseAction(higher, 2);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[same].Review, null));
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[higher].Review, null));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);
        return (scenario, list, same, higher);
    }

    [Fact]
    public async Task AWithdrawnRecordCanReturnOnlyAboveTheWithdrawnRevision()
    {
        var (_, list, same, higher) = await WithdrawnAtRevisionOneAsync();

        Assert.Equal([(same, 1, false), (higher, 1, true)],
            list.Items.Select(item => (item.RecordId, item.WithdrawnRevisionNumber!.Value, item.CanReturn)).OrderBy(item => item.RecordId));
    }

    [Fact]
    public async Task TheListAndThePublishLoaderAgreeOnWhichWithdrawnRecordsCanReturnSoNoCheckboxLeadsToARefusal()
    {
        var (scenario, list, _, _) = await WithdrawnAtRevisionOneAsync();

        foreach (var item in list.Items)
        {
            var publish = () => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
                new PublishReleaseRequest([item.Reference], $"r-{item.RecordId}") { Notes = [new(item.Kind, item.RecordId, "Back.")] });
            if (item.CanReturn) await publish();
            else Assert.Contains("Revise it before it returns", (await Assert.ThrowsAsync<ArgumentException>(publish)).Message, StringComparison.Ordinal);
        }
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

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([clean], list.Items.Select(item => item.RecordId));
    }

    [Fact]
    public async Task DraftThatIsNotApprovedIsNotReady()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a'), status: ReviewStatus.AwaitingReview));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task ReadyListIsBoundedByWhatOnePublishAcceptsAndSaysWhenMoreExist()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index <= PublishRelease.MaximumRecords; index++)
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(PublishRelease.MaximumRecords, list.Items.Length);
        Assert.True(list.HasMore);
        Assert.Equal(PublishRelease.MaximumRecords + 1, scenario.Reviews.UnpublishedLimit);
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
        var status = ReviewPolicy.GetCurrentStatus(review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns);
        scenario.Reviews.Unpublished.Add(new(new(draftId, review.CurrentRevision, review.ReviewStateVersion, status,
            review.UnresolvedConcerns.Length), null));

        var listed = (await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner)).Items.Length == 1;
        var published = await Record.ExceptionAsync(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draftId))) is null;

        Assert.Equal(publishable, listed);
        Assert.Equal(listed, published);
    }

    [Fact]
    public async Task ListHoldsDocumentChangesAndStatementsEachWithItsKindAndReplacementState()
    {
        var scenario = new PublicationScenario();
        scenario.Reviews.Unpublished.Add(Candidate(scenario.AddDraft('a')));
        scenario.Statements.Unpublished.Add(StatementCandidate(scenario.AddStatement('b'), publishedRevision: 1));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([PublishedRecordKind.DocumentChange, PublishedRecordKind.Statement], list.Items.Select(item => item.Kind));
        Assert.Equal([false, true], list.Items.Select(item => item.IsReplacement));
        Assert.Equal(new string('b', 64), list.Items[1].RecordId);
    }

    [Fact]
    public async Task StatementWithAnUnresolvedConcernIsNotReadyBecausePublishWouldRefuseIt()
    {
        var scenario = new PublicationScenario();
        var blocked = StatementCandidate(scenario.AddStatement('b'));
        scenario.Statements.Unpublished.Add(blocked with { Statement = blocked.Statement with { UnresolvedConcernCount = 1 } });

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task TheTotalAcrossBothKindsIsBoundedByWhatOnePublishAcceptsAndSaysWhenMoreExist()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index < 40; index++)
        {
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));
            scenario.Statements.Unpublished.Add(StatementCandidate(index.ToString("x64")));
        }

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(PublishRelease.MaximumRecords, list.Items.Length);
        Assert.True(list.HasMore);
    }

    [Fact]
    public async Task StatementStillAppearsWhenDocumentChangesAloneWouldFillThePublishLimit()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index < PublishRelease.MaximumRecords; index++)
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));
        var statementId = scenario.AddStatement('b');
        scenario.Statements.Unpublished.Add(StatementCandidate(statementId));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(PublishRelease.MaximumRecords, list.Items.Length);
        Assert.Contains(list.Items, item => item.Kind == PublishedRecordKind.Statement && item.RecordId == statementId);
        Assert.True(list.HasMore);
    }

    [Fact]
    public async Task KindWithFewReadyRecordsGivesItsUnusedShareToTheOtherKind()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index < 70; index++)
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));
        for (var index = 0; index < 10; index++)
            scenario.Statements.Unpublished.Add(StatementCandidate(index.ToString("x64")));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(54, list.Items.Count(item => item.Kind == PublishedRecordKind.DocumentChange));
        Assert.Equal(10, list.Items.Count(item => item.Kind == PublishedRecordKind.Statement));
        Assert.Equal(PublishRelease.MaximumRecords, list.Items.Length);
        Assert.True(list.HasMore);
    }

    [Fact]
    public async Task FourKindsShareTheLimitEquallyWhenEachHasMoreThanItsShare()
    {
        var scenario = new PublicationScenario();
        var action = ActionTemplate(scenario);
        var brief = BriefTemplate(scenario);
        for (var index = 0; index < 40; index++)
        {
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));
            scenario.Statements.Unpublished.Add(StatementCandidate(index.ToString("x64")));
            scenario.Actions.Unpublished.Add(ActionCandidate(action, (index + 100).ToString("x64")));
            scenario.Briefs.Unpublished.Add(new(brief with { RecordId = (index + 200).ToString("x64") }, null, false));
        }

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([16, 16, 16, 16, 0, 0, 0], KindCounts(list));
        Assert.True(list.HasMore);
        Assert.Equal(PublishRelease.MaximumRecords + 1, scenario.Actions.UnpublishedLimit);
        Assert.Equal(PublishRelease.MaximumRecords + 1, scenario.Briefs.UnpublishedLimit);
    }

    [Fact]
    public async Task AKindWithFewReadyRecordsGivesItsUnusedShareToTheOtherKinds()
    {
        var scenario = new PublicationScenario();
        var action = ActionTemplate(scenario);
        for (var index = 0; index < 70; index++)
        {
            scenario.Reviews.Unpublished.Add(Candidate(index.ToString("x32")));
            scenario.Actions.Unpublished.Add(ActionCandidate(action, (index + 100).ToString("x64")));
        }
        for (var index = 0; index < 5; index++)
            scenario.Statements.Unpublished.Add(StatementCandidate(index.ToString("x64")));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([29, 5, 30, 0, 0, 0, 0], KindCounts(list));
        Assert.True(list.HasMore);
    }

    [Fact]
    public async Task ApprovedVoteIsListedWithItsKindAndAVoteWithAConcernIsNot()
    {
        var scenario = new PublicationScenario();
        var approved = scenario.Votes.Publishable[scenario.AddVote('d')].Review;
        var concern = scenario.Votes.Publishable[scenario.AddVote('e', decisions: [PublicationScenario.Decision(new string('e', 64), 1, ReviewDecisionKind.RequestChanges)])].Review;
        scenario.Votes.Unpublished.AddRange([new(approved, null), new(concern, null)]);

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var item = Assert.Single(list.Items);
        Assert.Equal((PublishedRecordKind.Vote, approved.RecordId), (item.Kind, item.RecordId));
    }

    [Fact]
    public async Task ApprovedActionIsListedWithItsKindAndAnActionWithAConcernIsNot()
    {
        var scenario = new PublicationScenario();
        var action = ActionTemplate(scenario);
        scenario.Actions.Unpublished.Add(ActionCandidate(action, action.RecordId, publishedRevision: 1));
        scenario.Actions.Unpublished.Add(new(action with
        {
            RecordId = new string('e', 64),
            UnresolvedConcerns = [PublicationScenario.Decision(new string('e', 64), 2, ReviewDecisionKind.RequestChanges)]
        }, null));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var item = Assert.Single(list.Items);
        Assert.Equal((PublishedRecordKind.Action, action.RecordId, true), (item.Kind, item.RecordId, item.IsReplacement));
    }

    [Fact]
    public async Task AnApprovedBriefIsListedWithItsKindAndABriefWhoseCitedRecordsChangedIsNotBecausePublishWouldRefuseIt()
    {
        var scenario = new PublicationScenario();
        var brief = BriefTemplate(scenario);
        scenario.Briefs.Unpublished.Add(new(brief, 1, false));
        scenario.Briefs.Unpublished.Add(new(brief with { RecordId = new string('e', 64) }, null, true));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var item = Assert.Single(list.Items);
        Assert.Equal((PublishedRecordKind.Brief, brief.RecordId, true), (item.Kind, item.RecordId, item.IsReplacement));
    }

    [Fact]
    public async Task AnApprovedRelationshipIsListedWithBothHeadlinesAndOneWhoseCitedRecordsChangedIsNotBecausePublishWouldRefuseIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        var relationship = scenario.Relationships.Publishable[scenario.AddRelationship('e', statement, action)].Review;
        scenario.Relationships.Unpublished.Add(new(relationship, "Statement c", "Action d", 1, false));
        scenario.Relationships.Unpublished.Add(new(relationship with { RecordId = new string('f', 64) }, "Statement c", "Action d", null, true));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var item = Assert.Single(list.Items);
        Assert.Equal((PublishedRecordKind.Relationship, relationship.RecordId, "Statement c and Action d", true),
            (item.Kind, item.RecordId, item.Headline, item.IsReplacement));
    }

    [Fact]
    public async Task ARelationshipThatNoLongerMatchesIsListedWithTheFlagSoTheOwnerSeesItBeforePublishingButIsNotBlocked()
    {
        var scenario = new PublicationScenario();
        var relationship = scenario.Relationships.Publishable[scenario.AddRelationship('e', scenario.AddStatement('c'), scenario.AddAction('d'))].Review;
        scenario.Relationships.Unpublished.Add(new(relationship with { NoLongerMatches = true }, "Statement c", "Action d", null, false));
        scenario.Relationships.Unpublished.Add(new(relationship with { RecordId = new string('f', 64) }, "Statement c", "Action d", null, false));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal([(relationship.RecordId, true), (new string('f', 64), false)], list.Items.Select(item => (item.RecordId, item.NoLongerMatches)));
    }

    [Fact]
    public async Task ABriefWithAnUnresolvedConcernIsNotReady()
    {
        var scenario = new PublicationScenario();
        var brief = BriefTemplate(scenario);
        scenario.Briefs.Unpublished.Add(new(brief with
        {
            UnresolvedConcerns = [PublicationScenario.Decision(brief.RecordId, 2, ReviewDecisionKind.RequestChanges)]
        }, null, false));

        var list = await new ListPublishable(scenario.Loaders(), scenario.Publications, scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Empty(list.Items);
    }

    private static BriefReview BriefTemplate(PublicationScenario scenario) =>
        scenario.Briefs.Publishable[scenario.AddBrief(BriefFixtures.Week, [new(PublishedRecordKind.Statement, new string('1', 64), 1)])].Review;

    private static ActionReview ActionTemplate(PublicationScenario scenario) =>
        scenario.Actions.Publishable[scenario.AddAction('c')].Review;

    private static UnpublishedApprovedAction ActionCandidate(ActionReview template, string recordId, int? publishedRevision = null) =>
        new(template with { RecordId = recordId }, publishedRevision);

    private static int[] KindCounts(ReadyToPublishList list) =>
        [.. Enum.GetValues<PublishedRecordKind>().Select(kind => list.Items.Count(item => item.Kind == kind))];

    private static UnpublishedApprovedStatement StatementCandidate(string candidateId, int? publishedRevision = null,
        ReviewStatus status = ReviewStatus.Approved)
    {
        var revision = new StatementDraftRevision(candidateId, 1, "author", PublicationScenario.ApprovedAt, $"Statement {candidateId[0]}",
            "Summary", null, null, [], [], [], [], [], []);
        return new(new StatementReviewListItem(candidateId, "source", "https://example.test/", revision, 1, status, 0), publishedRevision);
    }

    private static UnpublishedApprovedDraft Candidate(string draftId, int? publishedRevision = null,
        ReviewStatus status = ReviewStatus.Approved)
    {
        var item = new DocumentChangeReviewListItem(draftId, new DocumentChangeDraftRevision(draftId, 1, "c", "author",
            PublicationScenario.ApprovedAt, $"Headline {draftId[0]}", "Summary", null, null, "City Council", null, null,
            [], [], []), 1, status, 0);
        return new(item, publishedRevision);
    }
}
