using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftStepTests
{
    private static readonly DateTimeOffset BothCovered = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly (string Date, string Quote) StatedDate = ("2026-10-03", "WASHINGTON, October 3, 2026");

    [Fact]
    public async Task AValidDraftWritesTheAiRevisionAndTheRunAndKeepsConfidenceOnlyInTheRun()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft(Output(date: StatedDate, quotes: ["a rent cap for tenants", "We welcome it."],
            otherSpeakers: ["Jo Rivera"], confidence: 0.77));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Succeeded, null), (entry.Status, entry.ErrorCode));
        var run = Assert.Single(store.Runs).Run;
        Assert.Equal(AnalysisRunOutcome.Drafted, run.Outcome);
        Assert.Contains("0.77", run.OutputJson, StringComparison.Ordinal);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, "civic-lens:analysis", entry.SubjectId), (revision.RevisionNumber, revision.AuthorSubject, revision.CandidateId));
        Assert.Equal([Warren], revision.SpeakerOfficialIds.ToArray());
        Assert.Equal(["Jo Rivera"], revision.OtherSpeakers.ToArray());
        Assert.Equal(["rent-affordability"], revision.IssueIds.ToArray());
        Assert.Equal(["housing-and-community-development"], revision.PolicyAreaIds.ToArray());
        Assert.Equal(["S. 1234", "H.R. 56"], revision.BillNumbers.ToArray());
        Assert.Equal(new DateOnly(2026, 10, 3), revision.StatementDate);
        Assert.Equal(Article.IndexOf(StatedDate.Quote, StringComparison.Ordinal), revision.StatementDateEvidence!.Start);
        Assert.Equal([StatementQuotationKind.ReleaseText, StatementQuotationKind.DirectQuotation], revision.Quotations.Select(item => item.Kind));
        Assert.DoesNotContain("onfidence", JsonSerializer.Serialize(revision), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADraftFromABillOrVotePassHasNoIssueIdsBecauseNoTrackedIssueMatched()
    {
        var carryOver = new StatementGateCarryOver([], ["housing-and-community-development"]);
        var store = new FakeStatementDraftStore(DraftEvidence(carryOver: carryOver));
        var model = new FakeStatementDraftModel().Draft(Output());

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal((State.Succeeded, null), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
        var revision = Assert.Single(store.Revisions);
        Assert.Empty(revision.IssueIds);
        Assert.Equal(["housing-and-community-development"], revision.PolicyAreaIds.ToArray());
    }

    [Fact]
    public async Task OneCoveredOfficialIsSetByCodeAndTheModelIsNotAskedForASpeaker()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft(Output());

        await Worker(store, model).ExecuteAsync(once: true);

        var request = Assert.Single(model.Requests);
        Assert.DoesNotContain("speakerOfficialIds", request.SchemaJson, StringComparison.Ordinal);
        Assert.Equal([Warren], Assert.Single(store.Revisions).SpeakerOfficialIds.ToArray());
    }

    [Fact]
    public async Task AModelSpeakerWhereCodeSetsTheSpeakerIsRejectedAndTheRetryRuns()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft(Output(speakers: [Warren])).Draft(Output());

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("Civic Lens sets the speaker", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
    }

    [Fact]
    public async Task SeveralCoveredOfficialsLetTheModelChooseFromAnEnumOfTheirIds()
    {
        var store = new FakeStatementDraftStore(DraftEvidence(observed: BothCovered));
        var model = new FakeStatementDraftModel().Draft(Output(speakers: [Markey]));

        await Worker(store, model).ExecuteAsync(once: true);

        var request = Assert.Single(model.Requests);
        using var schema = JsonDocument.Parse(request.SchemaJson);
        var choices = schema.RootElement.GetProperty("properties").GetProperty("speakerOfficialIds").GetProperty("items")
            .GetProperty("properties").GetProperty("id").GetProperty("enum").EnumerateArray().Select(item => item.GetString());
        Assert.Equal([Markey, Warren], choices);
        Assert.Equal([Markey], Assert.Single(store.Revisions).SpeakerOfficialIds.ToArray());
    }

    [Fact]
    public async Task ASpeakerOutsideTheEnumIsRejectedThenTheRetryRuns()
    {
        var store = new FakeStatementDraftStore(DraftEvidence(observed: BothCovered));
        var model = new FakeStatementDraftModel().Draft(Output(speakers: ["someone-else"])).Draft(Output(speakers: [Warren, Markey]));

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Contains("which is not a listed ID", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([Warren, Markey], Assert.Single(store.Revisions).SpeakerOfficialIds.ToArray());
        Assert.Equal(store.Runs[0].Run.RunId, store.Runs[1].Run.PreviousRunId);
    }

    [Fact]
    public async Task AnArticleThatStatesNoDateGivesADraftWithNoDate()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft(Output(date: null));

        await Worker(store, model).ExecuteAsync(once: true);

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((null, null), (revision.StatementDate, revision.StatementDateEvidence));
    }

    [Theory]
    [InlineData("a rent freeze for tenants", "does not occur in the article text")]
    [InlineData("Senator", "occurs more than once")]
    public async Task AWrongOrAmbiguousQuoteIsRejectedAsACitationErrorThenTheRetryRuns(string quote, string problem)
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft(Output(quotes: [quote])).Draft(Output());

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Contains(problem, model.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Equal([AnalysisRunOutcome.CitationRejected, AnalysisRunOutcome.Drafted], store.Runs.Select(run => run.Run.Outcome));
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task TwoRejectedAnswersFailTheEntryWithTheRejectionCodeLikeDocumentChangeDrafting()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Draft("not json").Draft(Output(quotes: ["a rent freeze"]));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Failed, "citationRejected"), (entry.Status, entry.ErrorCode));
        Assert.Equal([AnalysisRunOutcome.OutputRejected, AnalysisRunOutcome.CitationRejected], store.Runs.Select(run => run.Run.Outcome));
        Assert.Empty(store.Revisions);
        Assert.Empty(store.DraftsWithoutRevision);
    }

    [Fact]
    public async Task AnArticleOverTheByteCapMakesNoModelCallAndKeepsABypassedRunAndADraftWithoutARevision()
    {
        var store = new FakeStatementDraftStore(DraftEvidence(new string('x', 5_000)));
        var model = new FakeStatementDraftModel();
        var settings = new StatementDraftSettings(10_000_000, StatementDraftPrompt.MinimumReservation, 1);

        await Worker(store, model, settings).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Bypassed, StatementDraftErrorCodes.OverByteCap, 0L), (run.Run.Outcome, run.Reason, run.Run.ChargedTokens));
        Assert.Equal((State.Succeeded, "over-byte-cap"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
        Assert.Equal([store.Entries.Single().SubjectId], store.DraftsWithoutRevision);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AnExistingDraftSucceedsTheEntryWithoutACall()
    {
        var store = new FakeStatementDraftStore(DraftEvidence(existingDraftId: "someone"));
        var model = new FakeStatementDraftModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Succeeded, "humanDraftExists"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Fact]
    public async Task ASourceThatCoversNoOfficialOnTheArticleDateBlocksTheEntryWithoutACall()
    {
        var evidence = DraftEvidence(observed: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)) with
        {
            ConfigurationRevision = SpeakerRevision(warrenEndsBefore: new DateOnly(2026, 1, 1))
        };
        var store = new FakeStatementDraftStore(evidence);
        var model = new FakeStatementDraftModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, "speakersUnavailable"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Fact]
    public async Task AJobWithoutAConfigurationRevisionBlocksTheEntryWithoutACall()
    {
        var store = new FakeStatementDraftStore(DraftEvidence() with { ConfigurationRevision = null });
        var model = new FakeStatementDraftModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, "speakersUnavailable"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Theory]
    [InlineData(AnalysisModelFailure.AuthenticationFailed, "authenticationFailed")]
    [InlineData(AnalysisModelFailure.BillingFailed, "billingFailed")]
    public async Task AnAccountErrorPausesDraftingAndTheEntryStaysPending(AnalysisModelFailure failure, string reason)
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var status = new FakeAnalysisStatusStore();
        var model = new FakeStatementDraftModel().Fail(failure);

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.Equal((State.Pending, 0), (store.Entries.Single().Status, store.Entries.Single().Attempts));
        Assert.Equal((reason, (TimeSpan?)null), Assert.Single(status.Pauses));
    }

    [Fact]
    public async Task ARateLimitRetriesDurablyAndLeavesNoDraft()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        var model = new FakeStatementDraftModel().Fail(AnalysisModelFailure.RateLimited);

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Equal((State.RetryWaiting, "rateLimited"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task AnExhaustedDailyBudgetWaitsBeforeAnyCallAndResumesWhenTheBudgetReturns()
    {
        var store = new FakeStatementDraftStore(DraftEvidence()) { Charged = 99_000 };
        var model = new FakeStatementDraftModel().Draft(Output());
        var worker = Worker(store, model, new StatementDraftSettings(100_000, 50_000, 1));

        await worker.ExecuteAsync(once: true);
        Assert.Empty(model.Requests);
        Assert.Equal(State.WaitingForBudget, store.Entries.Single().Status);

        store.Charged = 0;
        await worker.ExecuteAsync(once: true);

        Assert.Single(model.Requests);
        Assert.Single(store.Revisions);
    }

    [Fact]
    public async Task AResumedRetryUsesThePreviousRejectionAndOnlyOneMoreCall()
    {
        var store = new FakeStatementDraftStore(DraftEvidence());
        store.ResumeAfterRejection("{}", "The quote \"x\" for \"quotations[0]\" does not occur in the article text.");
        var model = new FakeStatementDraftModel().Draft("not json");

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Contains("The quote \"x\"", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
        Assert.Equal((State.Failed, "outputRejected"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    private static AnalysisWorker<StatementDraftRun> Worker(FakeStatementDraftStore store, FakeStatementDraftModel model,
        StatementDraftSettings? settings = null, FakeAnalysisStatusStore? status = null) =>
        AnalysisWorkers.StatementDrafting(store, status ?? new FakeAnalysisStatusStore(), model,
            settings ?? new StatementDraftSettings(10_000_000, concurrency: 1), new FakeClock(DateTimeOffset.UnixEpoch));
}
