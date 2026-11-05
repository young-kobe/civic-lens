using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextMeasureTests
{
    private const long Cap = 10_000_000;
    private static readonly CongressBillListItem Bill = new(119, "hr", 1, default);
    private static readonly CongressBillListItem OtherBill = new(119, "s", 2, default);
    private static readonly ActionDraftSettings On = new(10_000_000, concurrency: 1, draftsFromBillText: true);

    [Fact]
    public async Task TheReportHoldsEachSentenceWithItsQuoteTheCharacterCountsAndTheTokensOfTheCall()
    {
        var (report, _, model) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()));

        var bill = Assert.Single(report.Bills);
        var text = Of();
        Assert.Equal(("119-hr-1", ActionTextMeasureOutcome.Accepted, null, text.CharsRead, text.CharsTotal, 1),
            (bill.BillId, bill.Outcome, bill.Reason, bill.CharsRead, bill.CharsTotal, bill.Calls));
        Assert.Equal((900, 100), (bill.Usage.InputTokens, bill.Usage.OutputTokens));
        Assert.Equal([(ActionTextPart.Summary, FirstPlain, FirstQuote), (ActionTextPart.Summary, SecondPlain, SecondQuote),
                (ActionTextPart.WhyItMatters, WhyPlain, WhyQuote)],
            bill.Sentences.Select(sentence => (sentence.Part, sentence.Sentence, sentence.Quote)));
        Assert.Empty(bill.Rejections);
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, 1, 1000L),
            (report.PromptVersion, report.SchemaVersion, report.Totals.Accepted, report.Totals.Usage.Total));
        Assert.Equal(ActionDraftTask.TextPromptVersion, Assert.Single(model.Requests).PromptVersion);
    }

    [Fact]
    public async Task ABillWithNoStoredTextIsSkippedWithItsReasonAndMakesNoCall()
    {
        var (report, _, model) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel(), withText: false);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NoBillText, 0), (bill.Outcome, bill.Reason, bill.Calls));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ABillWithNoCandidateIsSkippedAndTheNextBillStillRuns()
    {
        var (report, _, model) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()), bills: [OtherBill, Bill]);

        Assert.Equal([(ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NoCandidate), (ActionTextMeasureOutcome.Accepted, null)],
            report.Bills.Select(bill => (bill.Outcome, bill.Reason)));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task TheCapStopsTheRunBeforeACallThatCouldPassItAndLaterBillsAreNotRun()
    {
        var (report, _, model) = await MeasureAsync(1, new FakeHeadlineSummaryModel().Draft(Output()), bills: [Bill, OtherBill]);

        Assert.Equal([ActionTextMeasureOutcome.StoppedByCap, ActionTextMeasureOutcome.StoppedByCap], report.Bills.Select(bill => bill.Outcome));
        Assert.All(report.Bills, bill => Assert.Equal((ActionTextMeasureReason.SpendCap, 0), (bill.Reason, bill.Calls)));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TheCapCountsTheTokensOfEarlierBillsSoTheSecondCallStopsWhenTheRemainderIsTooSmall()
    {
        var reservation = (await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()))).Model.Requests[0].ReservedTokens;
        var cap = reservation + 999;

        var (report, _, model) = await MeasureAsync(cap, new FakeHeadlineSummaryModel().Draft(Output()).Draft(Output()),
            bills: [Bill, Bill]);

        Assert.Equal([ActionTextMeasureOutcome.Accepted, ActionTextMeasureOutcome.StoppedByCap], report.Bills.Select(bill => bill.Outcome));
        Assert.Single(model.Requests);
        Assert.True(report.Totals.Usage.Total <= cap);
    }

    [Fact]
    public async Task ADraftThatBreaksTheQuoteRuleIsRejectedAfterTheRetryAndEveryReasonIsReported()
    {
        var invented = Output([(FirstPlain, FirstQuote), ("The bill will pay every landlord in the country.", "pay every landlord in the country")]);

        var (report, _, model) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(invented).Draft(invented));

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Rejected, 2), (bill.Outcome, bill.Calls));
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(2, bill.Rejections.Length);
        Assert.Contains(bill.Rejections, reason => reason.Contains("quote", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(bill.Sentences);
        Assert.Equal(1800, bill.Usage.InputTokens);
    }

    [Fact]
    public async Task ABillThatHasASummaryIsSkippedWithNoCallBecauseTheStepWouldNotUseTheTextPrompt()
    {
        var world = ActionBillWorld.Create(summaries: [new("00", "2025-01-03", "2025-01-05T10:00:00Z", "<p>It funds rent aid.</p>")]);
        var evidence = world.Evidence() with { BillText = Of() };
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var report = await RunAsync(world, evidence, Cap, model, [Bill]);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Skipped, ActionTextMeasureReason.NotTextPrompt), (bill.Outcome, bill.Reason));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ABillWithACurrentTextRecordIsDraftedAgainBecauseTheMeasureClearsTheHistory()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var first = new FakeActionDraftStore(world.Evidence() with { BillText = Of() });
        await AnalysisWorkers.ActionDrafting(first, new FakeAnalysisStatusStore(), world.Reader(), new FakeHeadlineSummaryModel().Draft(Output()), On,
            BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        var current = Assert.Single(first.Revisions);
        var evidence = world.Evidence(current) with { BillText = Of() };
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var report = await RunAsync(world, evidence, Cap, model, [Bill]);

        Assert.Equal(ActionTextMeasureOutcome.Accepted, Assert.Single(report.Bills).Outcome);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task TheMeasureWritesNoRevisionNoRunAndNoQueueChangeToTheStore()
    {
        var (_, store, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Empty(store.Revisions);
        Assert.Empty(store.Runs);
        Assert.Empty(store.Requeues);
        Assert.Equal((AnalysisState.Pending, 0), (store.Entries[0].Status, store.Entries[0].Attempts));
    }

    [Fact]
    public async Task TheMeasureRefusesACapBelowOneToken()
    {
        var (store, step) = Parts(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ActionTextMeasure(store, step).RunAsync([Bill], 0, default));
    }

    private static async Task<(ActionTextMeasureReport Report, FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> MeasureAsync(long cap,
        FakeHeadlineSummaryModel model, bool withText = true, CongressBillListItem[]? bills = null)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = withText ? Of() : null };
        var (store, step) = Parts(world, model, evidence);
        return (await new ActionTextMeasure(store, step).RunAsync(bills ?? [Bill], cap, default), store, model);
    }

    private static Task<ActionTextMeasureReport> RunAsync(ActionBillWorld world, ActionDraftEvidence evidence, long cap, FakeHeadlineSummaryModel model,
        CongressBillListItem[] bills)
    {
        var (store, step) = Parts(world, model, evidence);
        return new ActionTextMeasure(store, step).RunAsync(bills, cap, default);
    }

    private static (FakeActionDraftStore Store, ActionDraftStep Step) Parts(ActionBillWorld world, FakeHeadlineSummaryModel model,
        ActionDraftEvidence? evidence = null)
    {
        var store = new FakeActionDraftStore(evidence ?? world.Evidence() with { BillText = Of() });
        return (store, new ActionDraftStep(store, world.Reader(), model, On, BillGateFixtures.Revision()));
    }
}
