using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Core.Analysis;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BriefFixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefDraftStepTests
{
    private static readonly BriefRecord[] Records =
    [
        Statement(new string('1', 64), "Warren on rent", 1, "warren"),
        DocumentChange(new string('2', 32), "A rule changed", 2)
    ];

    [Fact]
    public async Task TheFirstDraftCallsTheModelOnceAndWritesRevisionOneBoundToTheExactRevisionsItCites()
    {
        var (store, model) = await RunAsync(Records, new FakeBriefDraftModel().Draft(Output(paragraphs: [("Warren spoke.", ["R1"]), ("A rule.", ["R2"])])));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, "civic-lens:analysis", ReviewTextOrigin.Model, null), (revision.RevisionNumber, revision.AuthorSubject, revision.TextOrigin, revision.Suggestion));
        Assert.Equal((Week, 2, 3), (revision.Window.WeekStart, revision.Window.FirstReleaseNumber, revision.Window.CutoffReleaseNumber));
        Assert.Equal([(BriefCitationKind.DocumentChange, Records[1].RecordId, 2), (BriefCitationKind.Statement, Records[0].RecordId, 1)],
            revision.Citations.Select(citation => (citation.Kind, citation.RecordId, citation.RevisionNumber)).Order());
        Assert.Matches("^[0-9a-f]{64}$", revision.Fingerprint);
        Assert.Single(model.Requests);
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ThePromptListsOnlyTheOfficialsOfTheRecordsSentAndNoOtherConfiguredPerson()
    {
        var (_, model) = await RunAsync(Records, new FakeBriefDraftModel().Draft(Output()));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("Elizabeth Warren", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Bernie Sanders", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Ed Markey", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision()
    {
        var (store, _) = await RunAsync(Records, new FakeBriefDraftModel().Draft(Output(confidence: 0.77)));

        Assert.Contains("0.77", Assert.Single(store.Runs).Run.OutputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("onfidence", JsonSerializer.Serialize(Assert.Single(store.Revisions)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWeekWithNoNewRecordsMakesNoModelCallAndNoRevisionAndSettlesWithABypassedRun()
    {
        var (store, model) = await RunAsync([], new FakeBriefDraftModel());

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Succeeded, BriefDraftErrorCodes.NoNewRecords), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, BriefDraftErrorCodes.NoNewRecords, 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
    }

    [Fact]
    public async Task AWeekWhoseConsideredRecordsAreAllLeftOutBlocksInsteadOfClaimingThatNothingWasPublished()
    {
        var (store, model) = await RunAsync([], new FakeBriefDraftModel(), leftOut: 2);

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Empty(store.Runs);
        Assert.Equal((State.Blocked, BriefDraftErrorCodes.NoCitableRecords), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task TheSameRecordsAndCutoffSettleUnchangedWithoutACallBecauseTheFingerprintMatches()
    {
        var (first, _) = await RunAsync(Records, new FakeBriefDraftModel().Draft(Output()));

        var (second, model) = await RunAsync(Records, new FakeBriefDraftModel(), Assert.Single(first.Revisions));

        Assert.Empty(model.Requests);
        Assert.Empty(second.Revisions);
        Assert.Equal((State.Succeeded, BriefDraftErrorCodes.Unchanged), (second.Entries[0].Status, second.Entries[0].ErrorCode));
        Assert.Equal(("Bypassed", BriefDraftErrorCodes.Unchanged), (Assert.Single(second.Runs).Run.Outcome.ToString(), Assert.Single(second.Runs).Reason));
    }

    [Fact]
    public async Task ANewRecordOrANewRevisionOfARecordAppendsRevisionTwoWithANewFingerprint()
    {
        var (first, _) = await RunAsync(Records, new FakeBriefDraftModel().Draft(Output()));
        var current = Assert.Single(first.Revisions);
        var changed = new[] { Records[0], Records[1] with { RevisionNumber = 3 } };

        var (second, model) = await RunAsync(changed, new FakeBriefDraftModel().Draft(Output()), current);

        var revision = Assert.Single(second.Revisions);
        Assert.Single(model.Requests);
        Assert.Equal(2, revision.RevisionNumber);
        Assert.NotEqual(current.Fingerprint, revision.Fingerprint);
    }

    [Fact]
    public async Task AGroupThatAloneExceedsTheByteCapBlocksTheWeekWithoutACall()
    {
        var settings = new BriefDraftSettings(BriefDraftPrompt.MinimumReservation);

        var (store, model) = await RunAsync(Records, new FakeBriefDraftModel(), settings: settings);

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Blocked, BriefDraftErrorCodes.OverByteCap), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ARejectedAnswerGetsOneRetryThatCarriesTheReasons()
    {
        var model = new FakeBriefDraftModel().Draft(Output(paragraphs: [("Text.", ["R9"])])).Draft(Output());

        var (store, _) = await RunAsync(Records, model);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("\"R9\", which is not a listed ID", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.CitationRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task AWeekOfMoreRecordsThanABriefCanCiteOffersOnlyTheLimitSoAnAnswerCitingThemAllGetsTheCorrectiveRetry()
    {
        var many = Enumerable.Range(0, BriefRevision.MaximumCitations + 1).Select(index => Statement($"s{index:D4}")).ToArray();
        var everyId = Enumerable.Range(1, many.Length).Select(index => $"R{index}").ToArray();
        var model = new FakeBriefDraftModel().Draft(Output(paragraphs: [("Text.", everyId)])).Draft(Output());

        var (store, _) = await RunAsync(many, model);

        Assert.Equal([AnalysisRunOutcome.CitationRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Equal((many.Length, BriefRevision.MaximumCitations), (store.Revisions[0].Coverage.Considered, store.Revisions[0].Coverage.SentToModel));
    }

    [Fact]
    public async Task ASecondRejectionFailsTheWeekWithNoRevision()
    {
        var model = new FakeBriefDraftModel().Draft(Output(paragraphs: [("Text.", ["R9"])])).Draft(Output(paragraphs: [("Text.", [])]));

        var (store, _) = await RunAsync(Records, model);

        Assert.Equal(2, model.Requests.Count);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Failed, "citationRejected"), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task TheSelectionKeepsRoomForRetryReasonsSoARejectedAnswerStillGetsItsSecondCall()
    {
        var large = new[] { Statement(new string('1', 64), "First", 1, "warren") with { Summary = new string('a', 6_000) },
            Statement(new string('2', 64), "Second", 1, "warren") with { Summary = new string('b', 6_000) } };
        var fullBytes = BriefDraftPrompt.Build(Window(), SelectAll(large).Sent, Officials, int.MaxValue)!.ReservedTokens - BriefDraftTask.MaximumOutputTokens;
        var settings = new BriefDraftSettings(fullBytes + BriefDraftTask.MaximumOutputTokens, 1);
        var model = new FakeBriefDraftModel().Draft(Output(paragraphs: [("Text.", ["R9"])])).Draft(Output());

        var (store, _) = await RunAsync(large, model, settings: settings);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("\"R9\", which is not a listed ID", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(store.Revisions);
        Assert.Equal(1, store.Revisions[0].Coverage.SentToModel);
    }

    [Fact]
    public async Task TheWorkerQueuesClosedWeeksBeforeEveryPassSoAWeekThatJustEndedIsNotMissed()
    {
        var (store, _) = await RunAsync([], new FakeBriefDraftModel());

        Assert.True(store.EnqueueCalls >= 1);
    }

    private static async Task<(FakeBriefDraftStore Store, FakeBriefDraftModel Model)> RunAsync(IEnumerable<BriefRecord> records,
        FakeBriefDraftModel model, BriefRevision? current = null, BriefDraftSettings? settings = null, int leftOut = 0)
    {
        var store = new FakeBriefDraftStore(Evidence(records, current, leftOut: leftOut));
        await AnalysisWorkers.BriefDrafting(store, new FakeAnalysisStatusStore(), model, settings ?? new BriefDraftSettings(10_000_000, concurrency: 1),
            Officials, TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }

    private static string Output(string headline = "A week of housing news", double confidence = 0.8, params (string Text, string[] Ids)[] paragraphs) =>
        BriefFixtures.Output(headline, confidence, paragraphs);
}
