using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
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
        var grades = new[] { FirstPlain, SecondPlain, WhyPlain }.Select(ReadingGrade.Rounded).ToArray();
        Assert.Equal(grades, bill.Sentences.Select(sentence => sentence.Grade));
        var passage = ReadingGrade.RoundedPassage(["H.R. 1 pays landlords for tenants with low income.", FirstPlain, SecondPlain, WhyPlain]);
        Assert.Equal(passage, bill.PassageGrade);
        Assert.Equal(passage, report.Totals.MeanGrade);
        Assert.Equal((new BillFigureCounts(0, 1), new BillFigureCounts(0, 1), false, 0),
            (bill.TextFigures, bill.SentenceFigures, bill.LosesFigures, report.Totals.BillsLosingFigures));
        Assert.Empty(bill.MissingDates);
        Assert.Empty(bill.Rejections);
        Assert.False(string.IsNullOrWhiteSpace(bill.Title));
        Assert.Equal(Output(), Assert.Single(bill.Passes).OutputJson);
        Assert.Equal((ActionDraftTask.TextPromptVersion, ActionDraftTask.TextSchemaVersion, 1, 1000L),
            (report.PromptVersion, report.SchemaVersion, report.Totals.Accepted, report.Totals.Usage.Total));
        Assert.Equal(ActionDraftTask.TextPromptVersion, Assert.Single(model.Requests).PromptVersion);
    }

    [Fact]
    public async Task ABillWhoseTextHasAnAmountAndADateButWhoseSentencesHaveNoAmountIsFlaggedAndCountedInTheTotal()
    {
        var text = Of(Body + " The grant is $2,000,000.");
        var plain = Output([(FirstPlain, FirstQuote), (SecondPlain, SecondQuote)]);

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(plain), text: text);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((new BillFigureCounts(1, 1), new BillFigureCounts(0, 1), true, 1),
            (bill.TextFigures, bill.SentenceFigures, bill.LosesFigures, report.Totals.BillsLosingFigures));
    }

    [Fact]
    public async Task ABillWhoseTextHasADateButWhoseSentencesHaveNoDateIsFlaggedEvenWithNoAmount()
    {
        var plain = Output([(FirstPlain, FirstQuote), (SecondPlain, SecondQuote)], [(FirstPlain, FirstQuote)]);

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(plain));

        var bill = Assert.Single(report.Bills);
        Assert.Equal((new BillFigureCounts(0, 1), new BillFigureCounts(0, 0), true), (bill.TextFigures, bill.SentenceFigures, bill.LosesFigures));
        Assert.Equal<IEnumerable<string>>(["December 31, 2030"], bill.MissingDates);
    }

    [Fact]
    public async Task ABillThatKeepsOneDateOfTwoIsNotFlaggedAndListsNoMissingDateBecauseTheListIsForFlaggedBillsOnly()
    {
        var text = Of(Body + " The grant ends by June 1, 2031.");

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Output()), text: text);

        var bill = Assert.Single(report.Bills);
        Assert.Equal((2, false), (BillFigures.Count(text.Body).Dates, bill.LosesFigures));
        Assert.Empty(bill.MissingDates);
    }

    [Fact]
    public async Task TheFigureCountIgnoresTheHeaderLinesAndAnUnflaggedBillKeepsTheTotalAtZero()
    {
        var text = Of("Section 1. This Act may be cited as the Rent Aid Act. " + FirstSummary + " " + SecondSummary);
        var plain = Output([(FirstPlain, FirstQuote), (SecondPlain, SecondQuote)], [(FirstPlain, FirstQuote)]);

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(plain), text: text);

        var bill = Assert.Single(report.Bills);
        Assert.Contains("Version date: ", text.Text, StringComparison.Ordinal);
        Assert.Equal((new BillFigureCounts(0, 0), false, 0), (bill.TextFigures, bill.LosesFigures, report.Totals.BillsLosingFigures));
    }

    [Fact]
    public async Task ARejectedBillIsNeverFlaggedBecauseItHasNoAcceptedSentence()
    {
        var invented = Output([(FirstPlain, FirstQuote), ("The bill will pay every landlord.", "pay every landlord in the country")]);

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(invented).Draft(invented));

        var bill = Assert.Single(report.Bills);
        Assert.Equal((ActionTextMeasureOutcome.Rejected, false, 0), (bill.Outcome, bill.LosesFigures, report.Totals.BillsLosingFigures));
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
    public async Task ACancelKeepsTheBillsThatFinishedInAReportAndStillThrowsTheCancel()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Output()).Reply(_ => throw new OperationCanceledException());

        var thrown = await Assert.ThrowsAsync<ActionTextMeasureCanceledException>(() => MeasureAsync(Cap, model, bills: [Bill, Bill]));

        var bill = Assert.Single(thrown.Report.Bills);
        Assert.Equal(ActionTextMeasureOutcome.Accepted, bill.Outcome);
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
    public async Task TheMeasureRefusesACapBelowOneToken()
    {
        var (store, step) = Parts(ActionBillWorld.Create(summaries: []), new FakeHeadlineSummaryModel());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ActionTextMeasure(store, step).RunAsync([Bill], 0, default));
    }

    private static async Task<(ActionTextMeasureReport Report, FakeActionDraftStore Store, FakeHeadlineSummaryModel Model)> MeasureAsync(long cap,
        FakeHeadlineSummaryModel model, bool withText = true, CongressBillListItem[]? bills = null, ActionDraftBillText? text = null)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var evidence = world.Evidence() with { BillText = withText ? text ?? Of() : null };
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
