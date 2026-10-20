using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishCodeApprovedFactsTests
{
    private static readonly ReviewActor Code = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    public static TheoryData<PublishedRecordKind> JudgmentKinds => new(
        PublishedRecordKind.Statement, PublishedRecordKind.Relationship, PublishedRecordKind.Brief, PublishedRecordKind.DocumentChange);

    [Theory]
    [MemberData(nameof(JudgmentKinds))]
    public async Task TheCodeSubjectCannotPublishAJudgmentRecordBecauseOnlyAPersonApprovesJudgment(PublishedRecordKind kind)
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        var judgment = new PublishedRecordReference(kind, new string('b', 64));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Publisher().ExecuteAsync(Code,
            new([new(PublishedRecordKind.Action, action), judgment], "k")));

        Assert.Equal(0, scenario.Releases.BeginCount);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task TheCodeSubjectCanPublishActionsAndVotes()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        var vote = scenario.AddVote('b', ApprovedByCode('b'));

        var summary = await scenario.Publisher().ExecuteAsync(Code,
            new([new(PublishedRecordKind.Action, action), new(PublishedRecordKind.Vote, vote)], "k"));

        Assert.Equal(2, summary.RecordCount);
    }

    [Fact]
    public async Task TheAnalysisSubjectStillCannotPublishFactRecords()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Publisher().ExecuteAsync(
            new(ReviewAuthor.AnalysisSubject, ReviewRole.Owner), new([new(PublishedRecordKind.Action, action)], "k")));
    }

    [Fact]
    public async Task ATickPublishesEveryReadyFactInBatchesOfTheReleaseLimitAndThenStops()
    {
        var scenario = new PublicationScenario();
        var total = PublishRelease.MaximumRecords + 6;
        AddReadyActions(scenario, total);
        ListedUntilPublished(scenario);

        var published = await Facts(scenario).ExecuteAsync(default);

        Assert.Equal(total, published);
        Assert.Equal([PublishRelease.MaximumRecords, total], scenario.Publications.Committed.Select(release => release.RecordCount));
        Assert.Empty(scenario.Actions.Unpublished);
    }

    [Fact]
    public async Task NothingReadyMakesNoRelease()
    {
        var scenario = new PublicationScenario();

        Assert.Equal(0, await Facts(scenario).ExecuteAsync(default));

        Assert.Empty(scenario.Publications.Committed);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task ARecordThatAPersonApprovedIsNeverInAnAutomaticReleaseBecauseThePersonOwnsThatApproval()
    {
        var scenario = new PublicationScenario();
        var byCode = scenario.AddAction('a', ApprovedByCode('a'));
        var byPerson = scenario.AddAction('b');
        var voteByPerson = scenario.AddVote('c');
        AddUnpublished(scenario, byCode, byPerson);
        scenario.Votes.Unpublished.Add(new(scenario.Votes.Publishable[voteByPerson].Review, null));
        ListedUntilPublished(scenario);

        await Facts(scenario).ExecuteAsync(default);

        Assert.Equal([byCode], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Equal([byPerson], scenario.Actions.Unpublished.Select(item => item.Action.RecordId));
        Assert.Single(scenario.Votes.Unpublished);
    }

    [Fact]
    public async Task ARecordThatACodeApprovalReplacedWithAPersonApprovalWaitsForThePerson()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', [ApprovedByCode('a')[0], PublicationScenario.Decision(new string('a', 64), 2, ReviewDecisionKind.Approve)]);
        AddUnpublished(scenario, action);

        Assert.Equal(0, await Facts(scenario).ExecuteAsync(default));
    }

    [Fact]
    public async Task AConflictEndsTheTickWithoutAFailureBecauseTheNextTickBuildsAgainFromTheLatestRelease()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, 3);
        scenario.Publications.ConflictOnCommit = true;

        var published = await Facts(scenario).ExecuteAsync(default);

        Assert.Equal(0, published);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ARetryOfTheSameReadyRecordsReplaysTheStoredReleaseInsteadOfMakingAnother()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, 2);
        var facts = Facts(scenario);

        await facts.ExecuteAsync(default);
        await facts.ExecuteAsync(default);

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task AFailureThatIsNotAConflictSurfacesToTheCaller()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, 1);
        scenario.Publications.FailOnCommit = new InvalidOperationException("disk full");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Facts(scenario).ExecuteAsync(default));
    }

    private static PublishCodeApprovedFacts Facts(PublicationScenario scenario) => new(scenario.Publisher(), scenario.Loaders());

    private static ImmutableArray<ReviewDecision> ApprovedByCode(char id) =>
        [PublicationScenario.Decision(new string(id, 64), 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject }];

    private static void AddReadyActions(PublicationScenario scenario, int count)
    {
        var template = scenario.Actions.Publishable[scenario.AddAction('f', ApprovedByCode('f'))];
        scenario.Actions.Publishable.Clear();
        for (var index = 0; index < count; index++)
        {
            var recordId = index.ToString("x64");
            var revision = template.Review.CurrentRevision with { RecordId = recordId };
            var decisions = template.Review.Decisions.Select(decision => decision with { DraftId = recordId }).ToImmutableArray();
            var review = template.Review with
            {
                RecordId = recordId,
                CurrentRevision = revision,
                Revisions = [revision],
                Decisions = decisions
            };
            scenario.Actions.Publishable[recordId] = template with { Review = review };
            scenario.Actions.Unpublished.Add(new(review, null));
        }
    }

    private static void AddUnpublished(PublicationScenario scenario, params string[] actionIds) =>
        scenario.Actions.Unpublished.AddRange(actionIds.Select(id => new UnpublishedApprovedAction(scenario.Actions.Publishable[id].Review, null)));

    // A published record leaves the ready list, as it does in the database.
    private static void ListedUntilPublished(PublicationScenario scenario) =>
        scenario.Publications.BeforeServe = () =>
        {
            var published = scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId).ToHashSet();
            scenario.Actions.Unpublished.RemoveAll(item => published.Contains(item.Action.RecordId));
        };
}
