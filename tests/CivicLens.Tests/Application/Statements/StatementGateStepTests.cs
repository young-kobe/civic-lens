using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGateStepTests
{
    private const string Article = "The senator announced a rent cap for tenants. The office closes on Friday.";

    [Fact]
    public async Task AVerifiedTrackedIssueQueuesTheDraftAndRecordsTheQuoteSpan()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel().Classify(Output([HousingArea], 0.8, (RentIssue, "a rent cap")));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, StatementGateReasons.Passed), (entry.Status, entry.ErrorCode));
        Assert.Equal([entry.SubjectId], store.QueuedDrafts);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Classified, StatementGateReasons.Passed, Revision().Id), (run.Run.Outcome, run.Reason, run.IssueRevisionId));
        var quote = Assert.Single(run.Quotes);
        Assert.Equal((RentIssue, Article.IndexOf("a rent cap", StringComparison.Ordinal), "a rent cap".Length), (quote.IssueId, quote.Start, quote.Length));
    }

    [Fact]
    public async Task NoTrackedIssueSucceedsTheGateWithoutADraftAndKeepsPolicyAreasAndRubricRevision()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel().Classify(Output([HealthArea]));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, StatementGateReasons.NoTrackedIssue), (entry.Status, entry.ErrorCode));
        Assert.Empty(store.QueuedDrafts);
        var run = Assert.Single(store.Runs);
        Assert.Equal((StatementGateReasons.NoTrackedIssue, Revision().Id), (run.Reason, run.IssueRevisionId));
        Assert.Contains(HealthArea, run.Run.OutputJson, StringComparison.Ordinal);
        Assert.Contains(Revision().Id, run.Run.ContextJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillOrVoteYesQueuesTheDraftWithNoIssueQuotesAndTheStoredOutputKeepsTheAnswerForLaterLabels()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel().Classify(OutputNamingBillOrVote([HealthArea]));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, StatementGateReasons.PassedBillOrVote), (entry.Status, entry.ErrorCode));
        Assert.Equal([entry.SubjectId], store.QueuedDrafts);
        var run = Assert.Single(store.Runs);
        Assert.Empty(run.Quotes);
        Assert.Contains("\"namesBillOrVote\":true", run.Run.OutputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillNumberQueuesTheDraftEvenWhenTheModelAnswersNo()
    {
        var store = new FakeStatementGateStore(Article + " It follows H.R. 1234.");
        var model = new FakeStatementGateModel().Classify(Output());

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, StatementGateReasons.PassedBillNumber), (entry.Status, entry.ErrorCode));
        Assert.Equal([entry.SubjectId], store.QueuedDrafts);
    }

    [Fact]
    public async Task AnArticleOverTheByteCapMakesNoModelCallAndStillQueuesTheDraft()
    {
        var store = new FakeStatementGateStore(new string('x', 5_000));
        var model = new FakeStatementGateModel();
        var smallest = StatementGatePrompt.MinimumReservation;

        await Worker(store, model, new StatementGateSettings(smallest, 1)).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal([store.Entries.Single().SubjectId], store.QueuedDrafts);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, StatementGateReasons.OverByteCap, 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
        Assert.Equal(StatementGateReasons.OverByteCap, store.Entries.Single().ErrorCode);
    }

    [Fact]
    public async Task AnArticleWhenNoTrackedIssueIsActiveMakesNoModelCallAndSkips()
    {
        var store = new FakeStatementGateStore(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), Article);
        var model = new FakeStatementGateModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Empty(store.QueuedDrafts);
        Assert.Equal(StatementGateReasons.NoTrackedIssue, Assert.Single(store.Runs).Reason);
    }

    [Fact]
    public async Task InvalidOutputRetriesOnceWithTheRejectionReasonsThenSucceeds()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel()
            .Classify(Output([], 0.8, (RentIssue, "a rent freeze")))
            .Classify(Output([], 0.8, (RentIssue, "a rent cap")));

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("does not occur in the article text", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.CitationRejected, AnalysisRunOutcome.Classified], store.Runs.Select(run => run.Run.Outcome));
        Assert.Equal(store.Runs[0].Run.RunId, store.Runs[1].Run.PreviousRunId);
        Assert.Equal(StatementGateReasons.Passed, store.Entries.Single().ErrorCode);
    }

    [Fact]
    public async Task TwoInvalidOutputsFailOpenByQueueingTheDraftAndKeepingBothRuns()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel().Classify("not json").Classify(Output([], 2.5));

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal(2, model.Requests.Count);
        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, StatementGateReasons.FailedOpen), (entry.Status, entry.ErrorCode));
        Assert.Equal([entry.SubjectId], store.QueuedDrafts);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.OutputRejected], store.Runs.Select(run => run.Run.Outcome));
        Assert.Equal([null, StatementGateReasons.FailedOpen], store.Runs.Select(run => run.Reason));
    }

    [Theory]
    [InlineData(AnalysisModelFailure.AuthenticationFailed, "authenticationFailed")]
    [InlineData(AnalysisModelFailure.BillingFailed, "billingFailed")]
    public async Task AnAccountErrorPausesTheGateAndTheArticleWaitsWithoutADraft(AnalysisModelFailure failure, string reason)
    {
        var store = new FakeStatementGateStore(Article, Article + " More.");
        var status = new FakeAnalysisStatusStore();
        var model = new FakeStatementGateModel().Fail(failure);

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.Single(model.Requests);
        Assert.All(store.Entries, entry => Assert.Equal((State.Pending, 0), (entry.Status, entry.Attempts)));
        Assert.Empty(store.QueuedDrafts);
        Assert.Equal((reason, (TimeSpan?)null), Assert.Single(status.Pauses));
    }

    [Fact]
    public async Task ARateLimitRetriesDurablyAndIsNeverFailOpen()
    {
        var store = new FakeStatementGateStore(Article);
        var model = new FakeStatementGateModel().Fail(AnalysisModelFailure.RateLimited);

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal((State.RetryWaiting, "rateLimited"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
        Assert.Empty(store.QueuedDrafts);
    }

    [Fact]
    public async Task AnExhaustedDailyBudgetWaitsBeforeAnyCallAndResumesWhenTheBudgetReturns()
    {
        var store = new FakeStatementGateStore(Article) { Charged = 99_000 };
        var model = new FakeStatementGateModel().Classify(Output([], 0.8, (RentIssue, "a rent cap")));
        var worker = Worker(store, model, new StatementGateSettings(100_000, 1));

        await worker.ExecuteAsync(once: true);
        Assert.Empty(model.Requests);
        Assert.Equal(State.WaitingForBudget, store.Entries.Single().Status);

        store.Charged = 0;
        await worker.ExecuteAsync(once: true);

        Assert.Single(model.Requests);
        Assert.Equal((State.Succeeded, StatementGateReasons.Passed), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Fact]
    public async Task AResumedRetryUsesThePreviousRejectionAndOnlyOneMoreCall()
    {
        var store = new FakeStatementGateStore(Article);
        store.ResumeAfterRejection("{}", "The quote \"x\" does not occur in the article text.");
        var model = new FakeStatementGateModel().Classify("not json");

        await Worker(store, model).ExecuteAsync(once: true);

        var request = Assert.Single(model.Requests);
        Assert.Contains("The quote \"x\" does not occur", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(StatementGateReasons.FailedOpen, store.Entries.Single().ErrorCode);
    }

    private static AnalysisWorker<StatementGateRun> Worker(FakeStatementGateStore store, FakeStatementGateModel model,
        StatementGateSettings? settings = null, FakeAnalysisStatusStore? status = null) =>
        AnalysisWorkers.Gate(store, status ?? new FakeAnalysisStatusStore(), model, settings ?? new StatementGateSettings(10_000_000, concurrency: 1),
            Revision(), new FakeClock(DateTimeOffset.UnixEpoch));
}
