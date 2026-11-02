using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Application.Briefs.FakeBriefCheckModel;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefCheckStepTests
{
    private const string Note = "The paragraph says more than the record says.";

    [Fact]
    public async Task ACheckCallsTheModelOnceAndKeepsTheResultOnTheRunBoundToTheRevisionItChecked()
    {
        var (store, model) = await RunAsync(BriefCheckFixtures.Evidence(), new FakeBriefCheckModel().Check(Output("NeedsAttention", (1, "overstated", "R2", Note))));

        Assert.Single(model.Requests);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Drafted, 1, BriefCheckRecommendation.NeedsAttention), (run.Run.Outcome, run.Result!.RevisionNumber, run.Result.Recommendation));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ThePromptHoldsTheBriefTheCitedRecordsAndTheRecordsTheBriefLeavesOut()
    {
        var notCited = ImmutableArray.Create(BriefFixtures.Statement(new string('3', 64), "Markey on transit", 1, "markey"));

        var (_, model) = await RunAsync(BriefCheckFixtures.Evidence(notCited: notCited), new FakeBriefCheckModel().Check(Output("ApproveRecommended")));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("Warren backed a rent cap.", prompt, StringComparison.Ordinal);
        Assert.Contains("Warren on rent", prompt, StringComparison.Ordinal);
        Assert.Contains("Markey on transit", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameInputAsTheLastCheckMakesNoCallBecauseTheFingerprintIsTheInputHash()
    {
        var (first, _) = await RunAsync(BriefCheckFixtures.Evidence(), new FakeBriefCheckModel().Check(Output("ApproveRecommended")));
        var hash = Assert.Single(first.Runs).Run.InputHash;

        var (second, model) = await RunAsync(BriefCheckFixtures.Evidence(hash), new FakeBriefCheckModel());

        Assert.Empty(model.Requests);
        var run = Assert.Single(second.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, BriefDraftErrorCodes.Unchanged, 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
        Assert.Null(run.Result);
        Assert.Equal((State.Succeeded, BriefDraftErrorCodes.Unchanged), (second.Entries[0].Status, second.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ARevisionWithNewTextMakesANewCallEvenWhenAnEarlierCheckExists()
    {
        var (first, _) = await RunAsync(BriefCheckFixtures.Evidence(), new FakeBriefCheckModel().Check(Output("ApproveRecommended")));
        var changed = BriefCheckFixtures.Evidence(Assert.Single(first.Runs).Run.InputHash);
        changed = changed with { Revision = changed.Revision with { Headline = "A different headline", RevisionNumber = 2 } };

        var (_, model) = await RunAsync(changed, new FakeBriefCheckModel().Check(Output("ApproveRecommended")));

        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ABriefWithNoEvidenceBlocksWithoutACall()
    {
        var (store, model) = await RunAsync(null, new FakeBriefCheckModel());

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, BriefDraftErrorCodes.EvidenceUnavailable), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ARejectedAnswerIsRetriedOnceWithItsReasonsAndNothingIsStored()
    {
        var model = new FakeBriefCheckModel().Check(Output("NeedsAttention", (7, "overstated", null, Note))).Check(Output("ApproveRecommended"));

        var (store, _) = await RunAsync(BriefCheckFixtures.Evidence(), model);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("paragraph 7", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal(BriefCheckRecommendation.ApproveRecommended, store.Runs.Single(run => run.Result is not null).Result!.Recommendation);
    }

    [Fact]
    public async Task ATwiceRejectedAnswerFailsTheEntryAndStoresNoResult()
    {
        var bad = Output("NeedsAttention", (7, "overstated", null, Note));

        var (store, _) = await RunAsync(BriefCheckFixtures.Evidence(), new FakeBriefCheckModel().Check(bad).Check(bad));

        Assert.Equal(State.Failed, store.Entries[0].Status);
        Assert.All(store.Runs, run => Assert.Null(run.Result));
    }

    [Fact]
    public async Task APromptOverTheByteCapBlocksTheEntryWithoutACall()
    {
        var (store, model) = await RunAsync(BriefCheckFixtures.Evidence(), new FakeBriefCheckModel(),
            new BriefCheckSettings(BriefCheckPrompt.MinimumReservation, concurrency: 1));

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, BriefDraftErrorCodes.OverByteCap), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    private static async Task<(FakeBriefCheckStore Store, FakeBriefCheckModel Model)> RunAsync(BriefCheckEvidence? evidence,
        FakeBriefCheckModel model, BriefCheckSettings? settings = null)
    {
        var store = new FakeBriefCheckStore(evidence);
        await AnalysisWorkers.BriefChecking(store, new FakeAnalysisStatusStore(), model, settings ?? new BriefCheckSettings(10_000_000, concurrency: 1),
            TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
