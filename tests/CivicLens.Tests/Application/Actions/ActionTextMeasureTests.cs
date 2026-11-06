using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Spending;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextMeasureTests
{
    private const long Cap = 10_000_000;
    private static readonly CongressBillListItem Bill = new(119, "hr", 1, default);
    private static readonly CongressBillListItem OtherBill = new(119, "s", 2, default);
    private static readonly ModelPrice Price = new(ActionDraftTask.Model, [new(null, 1_000_000, 5_000_000, 0, 0)]);
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
        Assert.False(string.IsNullOrWhiteSpace(bill.Title));
        Assert.Equal(Output(), Assert.Single(bill.Passes).OutputJson);
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
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.OutputRejected], bill.Passes.Select(pass => pass.Outcome));
        Assert.All(bill.Passes, pass => Assert.Equal(invented, pass.OutputJson));
    }

    [Fact]
    public async Task AConnectionFailureChargesTheWholeReservationAndTheNextBillStopsByTheCap()
    {
        var reservation = (await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()))).Model.Requests[0].ReservedTokens;
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(AnalysisModelFailure.ConnectionFailed));

        var (report, _, _) = await MeasureAsync(reservation + 1, model, bills: [Bill, Bill]);

        Assert.Equal([ActionTextMeasureOutcome.Failed, ActionTextMeasureOutcome.StoppedByCap], report.Bills.Select(bill => bill.Outcome));
        Assert.Equal((1, reservation, AnalysisErrorCodes.ForOutcome(AnalysisRunOutcome.ConnectionFailed)),
            (report.Bills[0].Calls, report.Bills[0].ChargedTokens, report.Bills[0].Reason));
        Assert.Equal(reservation, report.Totals.ChargedTokens);
    }

    [Fact]
    public async Task AnInvalidResponseChargesItsUsageAndCountsInTheReport()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(AnalysisModelFailure.InvalidResponse, null, new(300, 40, 0, 0)));

        var (report, _, _) = await MeasureAsync(Cap, model);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Failed, 340L, 340L), (bill.Outcome, bill.ChargedTokens, bill.Usage.Total));
    }

    [Fact]
    public async Task AStopAtTheOutputLimitIsAFailureNotARejectionOfTheOutput()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ =>
            Task.FromResult(new AnalysisModelResponse("max_tokens", null, new(900, 100, 0, 0), "req_fixture")));

        var (report, _, _) = await MeasureAsync(Cap, model);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Failed, 1), (bill.Outcome, bill.Calls));
        Assert.Equal(AnalysisErrorCodes.ForOutcome(AnalysisRunOutcome.OutputLimitReached), bill.Reason);
    }

    [Fact]
    public async Task AFailureThatReportsMoreUsageThanItsChargeCountsTheRecordedChargeTowardTheCap()
    {
        var reservation = (await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()))).Model.Requests[0].ReservedTokens;
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(AnalysisModelFailure.RateLimited, null, new(300, 40, 0, 0)));

        var (report, _, _) = await MeasureAsync(reservation + 339, model, bills: [Bill, Bill]);

        Assert.Equal([ActionTextMeasureOutcome.Failed, ActionTextMeasureOutcome.StoppedByCap], report.Bills.Select(bill => bill.Outcome));
        Assert.Equal(340L, report.Totals.ChargedTokens);
    }

    [Fact]
    public async Task ARateLimitIsAFailureWithNoChargeAndTheNextBillStillRuns()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(AnalysisModelFailure.RateLimited)).Draft(Output());

        var (report, _, _) = await MeasureAsync(Cap, model, bills: [Bill, Bill]);

        Assert.Equal([ActionTextMeasureOutcome.Failed, ActionTextMeasureOutcome.Accepted], report.Bills.Select(bill => bill.Outcome));
        Assert.Equal(0, report.Bills[0].ChargedTokens);
    }

    [Theory]
    [InlineData(AnalysisModelFailure.AuthenticationFailed)]
    [InlineData(AnalysisModelFailure.BillingFailed)]
    [InlineData(AnalysisModelFailure.ModelNotFound)]
    public async Task AFailureThatNeedsAPersonStopsTheWholeRunLikeTheWorkerPause(AnalysisModelFailure kind)
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(kind)).Draft(Output());

        var (report, _, fake) = await MeasureAsync(Cap, model, bills: [Bill, Bill]);

        Assert.Equal([ActionTextMeasureOutcome.StoppedByModel, ActionTextMeasureOutcome.StoppedByModel], report.Bills.Select(bill => bill.Outcome));
        Assert.Equal(ActionTextMeasureReason.ModelFailure, report.Bills[1].Reason);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ACancelKeepsTheBillsThatFinishedInAReportAndChargesTheMonthOnceWithTheCallInFlightAtItsWholeReservation()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output()).Reply(_ => throw new OperationCanceledException());

        var thrown = await Assert.ThrowsAsync<ActionTextMeasureCanceledException>(() => MeasureAsync(Cap, model, bills: [Bill, Bill]));

        var bill = Assert.Single(kept!.Bills);
        Assert.Equal(ActionTextMeasureOutcome.Accepted, bill.Outcome);
        Assert.Null(thrown.ChargeFailure);
        Assert.Equal([1_400 + Price.WorstCaseMicros(model.Requests[1].ReservedTokens)], Spending.Charges);
    }

    [Fact]
    public async Task ACancelDuringTheRetryOfABillChargesItsFinishedPassAndTheCallInFlight()
    {
        var invented = Output([(FirstPlain, FirstQuote), ("The bill will pay every landlord in the country.", "pay every landlord in the country")]);
        var model = new FakeHeadlineSummaryModel().Draft(invented).Reply(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<ActionTextMeasureCanceledException>(() => MeasureAsync(Cap, model));

        Assert.Empty(kept!.Bills);
        Assert.Equal([1_400 + Price.WorstCaseMicros(model.Requests[1].ReservedTokens)], Spending.Charges);
    }

    [Fact]
    public async Task AnUnexpectedFaultAfterSomePaidBillsStillWritesTheReportAndChargesTheMonthOnce()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output()).Reply(_ => throw new InvalidOperationException("fault"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => MeasureAsync(Cap, model, bills: [Bill, Bill]));

        Assert.Equal("fault", thrown.Message);
        Assert.Equal(ActionTextMeasureOutcome.Accepted, Assert.Single(kept!.Bills).Outcome);
        Assert.Equal([1_400 + Price.WorstCaseMicros(model.Requests[1].ReservedTokens)], Spending.Charges);
    }

    [Fact]
    public async Task AChargeThatFailsAfterACancelStillThrowsTheCancelWithTheFailureAttachedAndKeepsTheReport()
    {
        var chargeFailure = new InvalidOperationException("charge");
        Spending = new FakeAiSpendingStore(Price) { ChargeFailure = chargeFailure };
        var model = new FakeHeadlineSummaryModel().Draft(Output()).Reply(_ => throw new OperationCanceledException());

        var thrown = await Assert.ThrowsAsync<ActionTextMeasureCanceledException>(() => MeasureAsync(Cap, model, bills: [Bill, Bill]));

        Assert.Same(chargeFailure, thrown.ChargeFailure);
        Assert.Single(kept!.Bills);
    }

    [Fact]
    public async Task ABillWithANewerCandidateIsNotHiddenAndIsSkippedWithTheStepReason()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence(hasNewerCandidate: true) with { BillText = Of() };
        var model = new FakeHeadlineSummaryModel().Draft(Output());

        var report = await RunAsync(world, evidence, Cap, model, [Bill]);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Skipped, ActionDraftErrorCodes.SupersededByNewerCandidate), (bill.Outcome, bill.Reason));
        Assert.Empty(model.Requests);
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
    public async Task TheMeasureAddsItsRealCostToTheMonthBecauseThePageShowsTheWholeSpend()
    {
        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()));

        Assert.Equal(1_000L, report.Totals.Usage.Total);
        Assert.Equal([1_400L], Spending.Charges);
    }

    [Fact]
    public async Task TheMeasureChargesEachBillAtItsOwnTierBecauseTwoSmallPromptsAreNotOneLargePrompt()
    {
        var tiered = new ModelPrice(ActionDraftTask.Model, [new(100_000, 1_000_000, 0, 0, 0), new(null, 2_000_000, 0, 0, 0)]);
        Spending = new FakeAiSpendingStore(tiered);
        var model = new FakeHeadlineSummaryModel();
        Reply(model);
        Reply(model);

        await MeasureAsync(Cap, model, bills: [Bill, Bill]);

        Assert.Equal([120_000L], Spending.Charges);

        void Reply(FakeHeadlineSummaryModel fake) =>
            fake.Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", Output(), new(60_000, 0, 0, 0), "req_fixture")));
    }

    [Fact]
    public async Task TheMeasureRefusesACapBelowOneToken()
    {
        var (store, step) = Parts(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RunAsync(new ActionTextMeasure(store, step, Spending), [Bill], 0));
    }

    private ActionTextMeasureReport? kept;

    private FakeAiSpendingStore Spending { get; set; } = new(Price);

    private async Task<(ActionTextMeasureReport Report, FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> MeasureAsync(long cap,
        FakeHeadlineSummaryModel model, bool withText = true, CongressBillListItem[]? bills = null)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = withText ? Of() : null };
        var (store, step) = Parts(world, model, evidence);
        await RunAsync(new ActionTextMeasure(store, step, Spending), bills ?? [Bill], cap);
        return (kept!, store, model);
    }

    private async Task<ActionTextMeasureReport> RunAsync(ActionBillWorld world, ActionDraftEvidence evidence, long cap, FakeHeadlineSummaryModel model,
        CongressBillListItem[] bills)
    {
        var (store, step) = Parts(world, model, evidence);
        await RunAsync(new ActionTextMeasure(store, step, Spending), bills, cap);
        return kept!;
    }

    private Task RunAsync(ActionTextMeasure measure, CongressBillListItem[] bills, long cap) =>
        measure.RunAsync(bills, cap, report =>
        {
            kept = report;
            return Task.CompletedTask;
        }, default);

    private static (FakeActionDraftStore Store, ActionDraftStep Step) Parts(ActionBillWorld world, FakeHeadlineSummaryModel model,
        ActionDraftEvidence? evidence = null)
    {
        var store = new FakeActionDraftStore(evidence ?? world.Evidence() with { BillText = Of() });
        return (store, new ActionDraftStep(store, world.Reader(), model, On, BillGateFixtures.Revision()));
    }
}
