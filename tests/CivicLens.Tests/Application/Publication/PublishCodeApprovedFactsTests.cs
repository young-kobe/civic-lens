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
    public async Task AfterARollbackATickMakesNoReleaseSoTheOwnerChoiceStands()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.Committed.Add(new(1, "000001", DateTimeOffset.UnixEpoch, 0));
        scenario.Publications.Committed.Add(new(2, "000002", DateTimeOffset.UnixEpoch, 0));
        scenario.Publications.ActiveReleaseNumber = 1;
        AddReadyActions(scenario, 1);

        Assert.Equal(0, await Facts(scenario).ExecuteAsync(default));

        Assert.Equal(2, scenario.Publications.Committed.Count);
    }

    [Fact]
    public async Task ARecordThatCannotBuildIsLeftOutAndReportedWhileTheOthersPublishInOneRelease()
    {
        var scenario = new PublicationScenario();
        var good = scenario.AddAction('a', ApprovedByCode('a'));
        var bad = scenario.AddAction('b', ApprovedByCode('b'), sponsorId: "nobody");
        var other = scenario.AddAction('c', ApprovedByCode('c'));
        AddUnpublished(scenario, good, bad, other);
        ListedUntilPublished(scenario);
        var reported = new List<Exception>();

        var published = await Facts(scenario, reported).ExecuteAsync(default);

        Assert.Equal(2, published);
        Assert.Equal([good, other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Single(scenario.Publications.Committed);
        var failure = Assert.Single(reported);
        Assert.Contains($"action:{bad}", failure.Message);
        Assert.Contains("no configured public name", failure.Message);
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

        var first = await facts.ExecuteAsync(default);
        var retry = await facts.ExecuteAsync(default);

        Assert.Equal((2, 0), (first, retry));
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

    private static PublishCodeApprovedFacts Facts(PublicationScenario scenario, List<Exception>? reported = null) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Loaders(), (reported ?? []).Add);

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
