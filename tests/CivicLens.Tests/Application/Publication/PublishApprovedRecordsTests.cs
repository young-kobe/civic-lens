using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application;
using CivicLens.Application.Actions;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Statements;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishApprovedRecordsTests
{
    private static readonly ReviewActor Code = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    [Fact]
    public async Task TheCodeSubjectCannotPublishAnEmptyRequestBecauseTheWorkerPublishesEveryKindOfRecord()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([new(PublishedRecordKind.Action, action)], "k1"));
        scenario.OfficialNames["mayor"] = "Mayor Renamed";

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(Code, new([], "k2")));

        Assert.Equal("request", refused.ParamName);

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task TheCodeSubjectCanPublishAnyKindBecauseTheWorkerPublishesEveryApprovedRecord()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        var statement = scenario.AddStatement('b');

        var summary = await scenario.Publisher().ExecuteAsync(Code,
            new([new(PublishedRecordKind.Action, action), new(PublishedRecordKind.Statement, statement)], "k"));

        Assert.Equal(2, summary.RecordCount);
    }

    [Fact]
    public async Task TheAnalysisSubjectStillCannotPublish()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Publisher().ExecuteAsync(
            new(ReviewAuthor.AnalysisSubject, ReviewRole.Owner), new([new(PublishedRecordKind.Action, action)], "k")));
    }

    [Fact]
    public async Task ATickPublishesEveryReadyRecordInBatchesOfTheReleaseLimitAndThenStops()
    {
        var scenario = new PublicationScenario();
        var total = PublishRelease.MaximumRecords + 6;
        AddReadyActions(scenario, total);
        ListedUntilPublished(scenario);

        var published = await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(total, published);
        Assert.Equal([PublishRelease.MaximumRecords, total], scenario.Publications.Committed.Select(release => release.RecordCount));
        Assert.Empty(scenario.Actions.Unpublished);
    }

    [Fact]
    public async Task AnActionThatReviewDoesNotApproveIsNotPublishedByATickBecauseOnlyAnApprovedRecordIsReady()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ImmutableArray.Create(
            PublicationScenario.Decision(new string('a', 64), 1, ReviewDecisionKind.RequestChanges)));
        AddUnpublished(scenario, action);
        ListedUntilPublished(scenario);

        var published = await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(0, published);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task NothingReadyMakesNoRelease()
    {
        var scenario = new PublicationScenario();

        Assert.Equal(0, await Publisher(scenario).ExecuteAsync(default));

        Assert.Empty(scenario.Publications.Committed);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task AStatementThatAPersonApprovedPublishesWithNoOwnerStepBecauseEveryApprovalPublishes()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('e');
        scenario.Statements.Unpublished.Add(StatementCandidate(statement));
        ListedUntilPublished(scenario);

        var published = await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([new PublishedRecordReference(PublishedRecordKind.Statement, statement)],
            scenario.Publications.LastCommit!.AddedRecords.Select(added => new PublishedRecordReference(added.Kind, added.RecordId)));
        Assert.Empty(scenario.Statements.Unpublished);
    }

    [Fact]
    public async Task APersonApprovalThatReplacesAPublishedRecordIsLeftOutWithALogUntilTheOwnerGivesANoteWhileTheOthersPublish()
    {
        var scenario = new PublicationScenario();
        var replaced = scenario.AddAction('a');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([new(PublishedRecordKind.Action, replaced)], "k1"));
        scenario.ReviseAction(replaced, 2, approverSubject: "reviewer", correctionNote: null);
        var fresh = scenario.AddAction('b', ApprovedByCode('b'));
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[replaced].Review, 1));
        AddUnpublished(scenario, fresh);
        ListedUntilPublished(scenario);
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([fresh], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Equal([replaced], scenario.Actions.Unpublished.Select(item => item.Action.RecordId));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        var line = Assert.Single(logger.Entries).Message;
        Assert.Contains($"action:{replaced}", line);
        Assert.Contains("correction note", line);
    }

    [Fact]
    public async Task APersonReplacementAtTheNotedCorrectionCapIsLeftOutWithALogWhileTheOthersPublish()
    {
        var scenario = new PublicationScenario();
        var capped = scenario.AddAction('a');
        var reference = new PublishedRecordReference(PublishedRecordKind.Action, capped);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([reference], "k1"));
        for (var revision = 2; revision <= PublishedCorrection.MaximumNotedEntries + 1; revision++)
        {
            scenario.ReviseAction(capped, revision);
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new([reference], $"k{revision}"));
        }
        scenario.ReviseAction(capped, PublishedCorrection.MaximumNotedEntries + 2);
        var fresh = scenario.AddAction('b', ApprovedByCode('b'));
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[capped].Review, PublishedCorrection.MaximumNotedEntries + 1));
        AddUnpublished(scenario, fresh);
        ListedUntilPublished(scenario);
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([fresh], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Equal([capped], scenario.Actions.Unpublished.Select(item => item.Action.RecordId));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains($"action:{capped}", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task ABriefWhoseCitedRecordsChangedDoesNotPublishButAStatementBesideItDoes()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('e');
        scenario.Statements.Unpublished.Add(StatementCandidate(statement));
        var brief = scenario.Briefs.Publishable[scenario.AddBrief(BriefFixtures.Week, [new(PublishedRecordKind.Statement, statement, 1)])].Review;
        scenario.Briefs.Unpublished.Add(new(brief, null, true));
        ListedUntilPublished(scenario);
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Empty(logger.Entries);
        Assert.Equal(PublishedRecordKind.Statement, Assert.Single(scenario.Publications.LastCommit!.AddedRecords).Kind);
        Assert.Single(scenario.Briefs.Unpublished);
    }

    [Fact]
    public async Task ManyRowsThatAreNotReadyDoNotHideAnOlderReadyRecordBecauseTheReadRepeatsWithoutThem()
    {
        var scenario = new PublicationScenario();
        for (var index = 0; index < PublishRelease.MaximumRecords + 6; index++)
        {
            var blocked = StatementCandidate(index.ToString("x64"));
            scenario.Statements.Unpublished.Add(blocked with { Statement = blocked.Statement with { UnresolvedConcernCount = 1 } });
        }
        var ready = scenario.AddStatement('e');
        scenario.Statements.Unpublished.Add(StatementCandidate(ready));
        ListedUntilPublished(scenario);

        var published = await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal(ready, Assert.Single(scenario.Publications.LastCommit!.AddedRecords).RecordId);
    }

    [Fact]
    public async Task EveryKindKeepsItsShareOfABatchSoOneLargeBacklogDoesNotCrowdOutTheOthers()
    {
        var scenario = new PublicationScenario();
        AddReadyStatements(scenario, PublishRelease.MaximumRecords + 6);
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        AddUnpublished(scenario, action);
        ListedUntilPublished(scenario);
        var firstBatch = new List<PublishedRecordReference>();
        var removePublished = scenario.Publications.BeforeServe!;
        scenario.Publications.BeforeServe = () =>
        {
            if (firstBatch.Count == 0)
                firstBatch.AddRange(scenario.Publications.LastCommit!.AddedRecords.Select(added => new PublishedRecordReference(added.Kind, added.RecordId)));
            removePublished();
        };

        await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(PublishRelease.MaximumRecords, firstBatch.Count);
        Assert.Contains(firstBatch, record => record.Kind == PublishedRecordKind.Action);
    }

    [Fact]
    public async Task AfterARollbackATickMakesNoReleaseSoTheOwnerChoiceStands()
    {
        var scenario = new PublicationScenario();
        scenario.Publications.Committed.Add(new(1, "000001", DateTimeOffset.UnixEpoch, 0));
        scenario.Publications.Committed.Add(new(2, "000002", DateTimeOffset.UnixEpoch, 0));
        scenario.Publications.ActiveReleaseNumber = 1;
        AddReadyActions(scenario, 1);

        Assert.Equal(0, await Publisher(scenario).ExecuteAsync(default));

        Assert.Equal(2, scenario.Publications.Committed.Count);
    }

    [Fact]
    public async Task EachBatchOfATickBeatsSoALongPublishDoesNotMakeTheWorkerLookDead()
    {
        var store = new RecordingHeartbeatStore();
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, PublishRelease.MaximumRecords + 6);

        await Publisher(scenario, beat: TestBeat.Create(store, TimeSpan.Zero)).ExecuteAsync(default);

        Assert.True(store.Beats >= 2);
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

        var published = await Publisher(scenario).ExecuteAsync(default);

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
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(2, published);
        Assert.Equal([good, other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.Single(scenario.Publications.Committed);
        var failure = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains($"action:{bad}", failure.Message);
        Assert.Contains("no configured public name", failure.Exception!.Message);
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
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        var line = Assert.Single(logger.Entries).Message;
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
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.Equal([other], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AConflictEndsTheTickWithoutAFailureBecauseTheNextTickBuildsAgainFromTheLatestRelease()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, 3);
        scenario.Publications.ConflictOnCommit = true;

        var published = await Publisher(scenario).ExecuteAsync(default);

        Assert.Equal(0, published);
        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ARetryOfTheSameReadyRecordsReplaysTheStoredReleaseInsteadOfMakingAnother()
    {
        var scenario = new PublicationScenario();
        AddReadyActions(scenario, 2);
        var facts = Publisher(scenario);

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(scenario).ExecuteAsync(default));
    }

    [Fact]
    public async Task ATickThatReplacesAPublishedRecordWritesAnUpdatedFromTheSourceEntryWithoutANote()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', ApprovedByCode('a'));
        AddUnpublished(scenario, action);
        ListedUntilPublished(scenario);
        await Publisher(scenario).ExecuteAsync(default);
        scenario.ReviseAction(action, 2);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[action].Review, 1));
        var releases = scenario.Publications.Committed.Count;
        var logger = new CapturingLogger<PublishApprovedRecords>();

        await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);

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
            scenario.ReviseAction(action, revision, correctionNote: "Fix.");
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([reference], $"k{revision}"));
        }
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2, ReviewAuthor.CodeSubject);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[action].Review, PublishedCorrection.MaximumNotedEntries + 1));
        ListedUntilPublished(scenario);
        var logger = new CapturingLogger<PublishApprovedRecords>();

        var published = await Publisher(scenario, logger).ExecuteAsync(default);

        Assert.Equal(1, published);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    private static PublishApprovedRecords Publisher(PublicationScenario scenario, CapturingLogger<PublishApprovedRecords>? logger = null,
        WorkerBeat? beat = null) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Loaders(), logger ?? new CapturingLogger<PublishApprovedRecords>(),
            beat ?? TestBeat.Create());

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

    private static void AddReadyStatements(PublicationScenario scenario, int count)
    {
        var template = scenario.Statements.Publishable[scenario.AddStatement('f')];
        scenario.Statements.Publishable.Clear();
        for (var index = 0; index < count; index++)
        {
            var candidateId = index.ToString("x64");
            var revision = template.Review.CurrentRevision! with { CandidateId = candidateId };
            var review = template.Review with
            {
                CandidateId = candidateId,
                CurrentRevision = revision,
                Revisions = [revision],
                Decisions = [.. template.Review.Decisions.Select(decision => decision with { DraftId = candidateId })]
            };
            scenario.Statements.Publishable[candidateId] = template with { Review = review };
            scenario.Statements.Unpublished.Add(StatementCandidate(candidateId));
        }
    }

    private static UnpublishedApprovedStatement StatementCandidate(string candidateId)
    {
        var revision = new StatementDraftRevision(candidateId, 1, "author", PublicationScenario.ApprovedAt, $"Statement {candidateId[0]}",
            "Summary", null, null, [], [], [], [], [], []);
        return new(new StatementReviewListItem(candidateId, "source", "https://example.test/", revision, 1, ReviewStatus.Approved, 0), null);
    }

    // A published record leaves the ready list, as it does in the database.
    private static void ListedUntilPublished(PublicationScenario scenario) =>
        scenario.Publications.BeforeServe = () =>
        {
            var published = scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId).ToHashSet();
            scenario.Actions.Unpublished.RemoveAll(item => published.Contains(item.Action.RecordId));
            scenario.Statements.Unpublished.RemoveAll(item => published.Contains(item.Statement.CandidateId));
        };
}
