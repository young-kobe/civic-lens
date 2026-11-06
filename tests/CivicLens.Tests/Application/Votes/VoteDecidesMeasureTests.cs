using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.VoteWorld;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDecidesMeasureTests
{
    private const long Cap = 10_000_000;
    private const string Html = "This bill gives $50 million to the Department of Housing for rent aid in 2026. It starts on January 1, 2026.";
    private const string Decides = "The bill gives $50 million to the Department of Housing for rent aid in 2026. The question passed.";
    private const string Quote = "gives $50 million to the Department of Housing for rent aid in 2026";
    private static readonly VoteMeasureRoll Roll = new(Chamber.House, 119, 1, 100);

    private static string Answer(string text = Decides) =>
        HeadlineSummaryOutputs.Output("The House voted on a concurrent resolution", "The House decided whether to agree to a Senate amendment.",
            issueIds: ["rent"], decides: (text, Quote));

    [Fact]
    public async Task TheReportHoldsTheAcceptedTextItsQuoteItsGradeTheModeAndTheTokensOfTheCall()
    {
        var (report, store, model) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Answer()));

        var vote = Assert.Single(report.Votes);
        Assert.Equal(("house-119-1-100", AnalysisMeasureOutcome.Accepted, null, "Content", 1), (vote.VoteId, vote.Outcome, vote.Reason, vote.Mode, vote.Calls));
        Assert.Equal((Decides, Quote, ReadingGrade.Rounded(Decides)), (vote.Decides, vote.Quote, vote.Grade));
        Assert.Equal((900, 100), (vote.Usage.InputTokens, vote.Usage.OutputTokens));
        Assert.Equal((VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, 1, 1, 0, 1000L, ReadingGrade.Rounded(Decides)),
            (report.PromptVersion, report.SchemaVersion, report.Totals.Accepted, report.Totals.ContentVotes, report.Totals.ProcedureVotes,
                report.Totals.Usage.Total, report.Totals.MeanGrade));
        Assert.Equal(Answer(), Assert.Single(vote.Passes).OutputJson);
        Assert.Empty(store.Revisions);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task AProcedureVoteCountsAsProcedureInTheTotals()
    {
        var answer = HeadlineSummaryOutputs.Output("The House voted on a motion", "The House decided whether to send the bill back.", issueIds: ["rent"],
            decides: ("The vote decides whether to send the bill back to committee.", "gives $50 million to the Department of Housing"));

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(answer), world: Create(question: "On Motion to Recommit", summaryHtml: Html));

        Assert.Equal(("Procedure", 0, 1), (Assert.Single(report.Votes).Mode, report.Totals.ContentVotes, report.Totals.ProcedureVotes));
    }

    [Fact]
    public async Task AnAnswerThatFailsTheChecksTwiceIsRejectedWithItsReasons()
    {
        var bad = Answer("The bill gives $60 million to rent aid.");

        var (report, _, _) = await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(bad).Draft(bad));

        var vote = Assert.Single(report.Votes);
        Assert.Equal((AnalysisMeasureOutcome.Rejected, 2), (vote.Outcome, vote.Calls));
        Assert.Contains(vote.Rejections, reason => reason.Contains("states $60 million", StringComparison.Ordinal));
        Assert.Equal(2, vote.Passes.Length);
    }

    [Fact]
    public async Task AStopAtTheOutputLimitIsAFailureNotARejectionOfTheOutput()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ =>
            Task.FromResult(new AnalysisModelResponse("max_tokens", null, new(900, 100, 0, 0), "req_fixture")));

        var (report, _, _) = await MeasureAsync(Cap, model);

        var vote = Assert.Single(report.Votes);
        Assert.Equal((AnalysisMeasureOutcome.Failed, 1), (vote.Outcome, vote.Calls));
        Assert.Equal(AnalysisErrorCodes.ForOutcome(AnalysisRunOutcome.OutputLimitReached), vote.Reason);
    }

    [Fact]
    public async Task ARefusalOfTheModelIsAFailureToo()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => Task.FromResult(new AnalysisModelResponse("refusal", null, new(900, 100, 0, 0), "req_fixture")));

        var (report, _, _) = await MeasureAsync(Cap, model);

        Assert.Equal(AnalysisMeasureOutcome.Failed, Assert.Single(report.Votes).Outcome);
    }

    [Fact]
    public async Task ARateLimitIsAFailureWithNoChargeAndTheNextVoteStillRuns()
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(AnalysisModelFailure.RateLimited)).Draft(Answer());

        var (report, _, _) = await MeasureAsync(Cap, model, rolls: [Roll, Roll]);

        Assert.Equal([AnalysisMeasureOutcome.Failed, AnalysisMeasureOutcome.Accepted], report.Votes.Select(vote => vote.Outcome));
        Assert.Equal(0, report.Votes[0].ChargedTokens);
    }

    [Theory]
    [InlineData(AnalysisModelFailure.AuthenticationFailed)]
    [InlineData(AnalysisModelFailure.BillingFailed)]
    [InlineData(AnalysisModelFailure.ModelNotFound)]
    public async Task AFailureThatNeedsAPersonStopsTheWholeRunLikeTheWorkerPause(AnalysisModelFailure kind)
    {
        var model = new FakeHeadlineSummaryModel().Reply(_ => throw new AnalysisModelException(kind)).Draft(Answer());

        var (report, _, fake) = await MeasureAsync(Cap, model, rolls: [Roll, Roll]);

        Assert.Equal([AnalysisMeasureOutcome.StoppedByModel, AnalysisMeasureOutcome.StoppedByModel], report.Votes.Select(vote => vote.Outcome));
        Assert.Equal(VoteDecidesMeasureReason.ModelFailure, report.Votes[1].Reason);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task TheSpendCapStopsBeforeACallThatCouldPassItAndTheLaterVotesDoNotRun()
    {
        var reservation = (await MeasureAsync(Cap, new FakeHeadlineSummaryModel().Draft(Answer()))).Model.Requests[0].ReservedTokens;

        var (report, _, model) = await MeasureAsync(reservation + 999, new FakeHeadlineSummaryModel().Draft(Answer()).Draft(Answer()), rolls: [Roll, Roll, Roll]);

        Assert.Equal([AnalysisMeasureOutcome.Accepted, AnalysisMeasureOutcome.StoppedByCap, AnalysisMeasureOutcome.StoppedByCap], report.Votes.Select(vote => vote.Outcome));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ACancelKeepsTheVotesThatFinishedInAReportAndStillThrowsTheCancel()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Answer()).Reply(_ => throw new OperationCanceledException());

        var thrown = await Assert.ThrowsAsync<VoteDecidesMeasureCanceledException>(() => MeasureAsync(Cap, model, rolls: [Roll, Roll]));

        Assert.Equal(AnalysisMeasureOutcome.Accepted, Assert.Single(thrown.Report.Votes).Outcome);
    }

    [Fact]
    public async Task ARollCallWithNoCandidateIsSkippedWithNoCall()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Answer());

        var (report, _, _) = await MeasureAsync(Cap, model, rolls: [new(Chamber.House, 119, 1, 999)]);

        var vote = Assert.Single(report.Votes);
        Assert.Equal((AnalysisMeasureOutcome.Skipped, VoteDecidesMeasureReason.NoCandidate, 0), (vote.Outcome, vote.Reason, vote.Calls));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task AVoteWhoseBillHasNoSavedSummaryIsSkippedWithNoCallBecauseNothingCouldBackAQuote()
    {
        var model = new FakeHeadlineSummaryModel().Draft(Answer());

        var (report, _, _) = await MeasureAsync(Cap, model, world: Create(summaryHtml: null));

        var vote = Assert.Single(report.Votes);
        Assert.Equal((AnalysisMeasureOutcome.Skipped, VoteDecidesMeasureReason.NoBillSummary), (vote.Outcome, vote.Reason));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task AVoteWithACurrentRevisionIsDraftedAgainAsANewVoteAndNothingIsStored()
    {
        var world = Create(summaryHtml: Html);
        var first = new FakeVoteDraftStore(world.Evidence());
        await AnalysisWorkers.VoteDrafting(first, new FakeAnalysisStatusStore(), world.Reader(), new FakeHeadlineSummaryModel().Draft(Answer()),
            new VoteDraftSettings(Cap, concurrency: 1), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        var current = Assert.Single(first.Revisions);
        var model = new FakeHeadlineSummaryModel().Draft(Answer());

        var (report, store, _) = await MeasureAsync(Cap, model, world: world, current: current);

        Assert.Equal(AnalysisMeasureOutcome.Accepted, Assert.Single(report.Votes).Outcome);
        Assert.Single(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Theory]
    [InlineData("house-119-1-100", Chamber.House, 119, 1, 100)]
    [InlineData("senate-119-2-45", Chamber.Senate, 119, 2, 45)]
    public void ARollCallIdReadsBackToTheSameRollCall(string id, Chamber chamber, int congress, int session, int roll)
    {
        var parsed = VoteMeasureRoll.TryParse(id)!;

        Assert.Equal((chamber, congress, session, roll, id), (parsed.Chamber, parsed.Congress, parsed.SessionNumber, parsed.RollCallNumber, parsed.Id));
    }

    [Theory]
    [InlineData("house-119-3-1")]
    [InlineData("house-119-1-0")]
    [InlineData("house-0-1-1")]
    [InlineData("congress-119-1-1")]
    [InlineData("House-119-1-1")]
    [InlineData("house-119-01-1")]
    [InlineData("house-119-1")]
    [InlineData("house-119-1-1-1")]
    public void AnIdInAnyOtherFormIsRefused(string id) => Assert.Null(VoteMeasureRoll.TryParse(id));

    private static async Task<(VoteDecidesMeasureReport Report, FakeVoteDraftStore Store, FakeHeadlineSummaryModel Model)> MeasureAsync(long cap,
        FakeHeadlineSummaryModel model, VoteWorld? world = null, IReadOnlyList<VoteMeasureRoll>? rolls = null, VoteDraftRevision? current = null)
    {
        world ??= Create(summaryHtml: Html);
        var store = new FakeVoteDraftStore(world.Evidence(current));
        var step = new VoteDraftStep(store, world.Reader(), model, new VoteDraftSettings(Cap, concurrency: 1), BillGateFixtures.Revision());
        var report = await new VoteDecidesMeasure(store, step).RunAsync(rolls ?? [Roll], cap, default);
        return (report, store, model);
    }
}
