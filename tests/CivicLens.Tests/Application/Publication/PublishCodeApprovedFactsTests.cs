using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
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
    public async Task ARollbackBetweenBatchesStopsTheTickSoTheNextBatchDoesNotUndoIt()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, PublishRelease.MaximumRecords + 6);
        ListedUntilPublished(scenario);
        var removePublished = scenario.Publications.BeforeServe!;
        scenario.Publications.BeforeServe = () =>
        {
            removePublished();
            scenario.Publications.Committed.Add(new(2, "000002", DateTimeOffset.UnixEpoch, 0));
        };

        var published = await Facts(scenario).ExecuteAsync(default);

        Assert.Equal(PublishRelease.MaximumRecords, published);
        Assert.Equal([1, 2], scenario.Publications.Committed.Select(release => release.ReleaseNumber));
        Assert.Equal(6, scenario.Actions.Unpublished.Count);
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
    public async Task AWithdrawnRecordIsLeftOutWithALogAndNoFailureSoATickNeverPublishesItAgainAndNobodyIsPaged()
    {
        var scenario = new PublicationScenario();
        var withdrawn = scenario.AddAction('a', ApprovedByCode('a'));
        var other = scenario.AddAction('b', ApprovedByCode('b'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([new(PublishedRecordKind.Action, withdrawn)], "k1"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Action, withdrawn, "Wrong.")] });
        AddUnpublished(scenario, withdrawn, other);
        ListedUntilPublished(scenario);
        var reported = new List<Exception>();
        var logged = new List<string>();

        var published = await Facts(scenario, reported, logged).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Empty(reported);
        var line = Assert.Single(logged);
        Assert.Contains($"action:{withdrawn}", line);
        Assert.Contains("withdrawn", line);
    }

    [Fact]
    public async Task AWithdrawnBacklogLargerThanTheBatchLimitDoesNotStarveTheOtherFactRecords()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, PageLimit.Maximum + 1);
        var backlog = scenario.Actions.Publishable.Keys.Order(StringComparer.Ordinal).ToArray();
        foreach (var batch in backlog.Chunk(PublishRelease.MaximumRecords).Select((ids, index) => (ids, index)))
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([.. batch.ids.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))], $"p{batch.index}"));
        foreach (var batch in backlog.Chunk(PublishRelease.MaximumRecords).Select((ids, index) => (ids, index)))
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], $"w{batch.index}")
            {
                Withdrawals = [.. batch.ids.Select(id => new RecordNote(PublishedRecordKind.Action, id, "Wrong."))]
            });
        var other = scenario.AddAction('b', ApprovedByCode('b'));
        scenario.Actions.Unpublished.Clear();
        AddUnpublished(scenario, [.. backlog, other]);
        ListedUntilPublished(scenario);
        var reported = new List<Exception>();

        var published = await Facts(scenario, reported).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Empty(reported);
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

    [Fact]
    public async Task ATickThatReplacesAPublishedRecordWritesAnUpdatedFromTheSourceEntryWithoutANote()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        AddUnpublished(scenario, action);
        ListedUntilPublished(scenario);
        await Facts(scenario).ExecuteAsync(default);
        scenario.ReviseAction(action, 2);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[action].Review, 1));
        var releases = scenario.Publications.Committed.Count;
        var reported = new List<Exception>();

        await Facts(scenario, reported).ExecuteAsync(default);

        Assert.Empty(reported);

        var directory = scenario.Publications.Committed[releases].DirectoryName;
        var record = JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(action)], PublicationProtocol.JsonOptions)!;
        var entry = Assert.Single(record.Corrections);
        Assert.Equal((1, null), (entry.ReplacedRevisionNumber, entry.Note));
    }

    [Fact]
    public async Task ATickPublishesAnUpdateOfARecordThatHasTheMostPersonCorrectionsInsteadOfSkippingIt()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a');
        var reference = new PublishedRecordReference(PublishedRecordKind.Action, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([reference], "k1"));
        for (var revision = 2; revision <= PublishedCorrection.MaximumNotedEntries + 1; revision++)
        {
            scenario.ReviseAction(action, revision);
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
                new PublishReleaseRequest([reference], $"k{revision}") { Notes = [new(reference.Kind, action, "Fix.")] });
        }
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2, ReviewAuthor.CodeSubject);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[action].Review, PublishedCorrection.MaximumNotedEntries + 1));
        ListedUntilPublished(scenario);
        var reported = new List<Exception>();

        var published = await Facts(scenario, reported).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Empty(reported);
    }

    private static PublishCodeApprovedFacts Facts(PublicationScenario scenario, List<Exception>? reported = null, List<string>? logged = null) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Loaders(), (reported ?? []).Add, (logged ?? []).Add);

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
