using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeAnalysisStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DocumentChangeDraftCatalog Catalog = new([], []);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-analysis-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private PostgresDocumentChangeAnalysisStore analysis = null!;
    private ComparisonSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        analysis = new PostgresDocumentChangeAnalysisStore(factory);
        seeder = new ComparisonSeeder(attempts, new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task EvidenceCarriesTheConfigurationRevisionOfTheJobThatCollectedTheLaterVersion()
    {
        var seeder = new StatementCandidateSeeder(attempts, new PostgresDocumentExtractionStore(factory), connectionString, captureRoot);
        var revision = StatementDraftFixtures.SpeakerRevision();
        var revisionId = await seeder.StoreRevisionAsync(revision);
        var definition = JsonSerializer.Serialize(StatementDraftFixtures.AdmittedDefinition(revision.Configuration) with { ConfigurationRevision = null },
            CollectionProtocol.JsonOptions);
        const string url = "https://example.test/pages/a";
        var before = await seeder.ImportAsync("Deadline: October 15.\n", url, CandidateJobKind.WatchedPage);
        var after = await seeder.ImportAsync("Deadline: October 30.\n", url, CandidateJobKind.WatchedPage, definitionJson: definition, revisionId: revisionId);
        var extractions = new PostgresDocumentExtractionStore(factory);
        var beforeExtraction = new DocumentExtraction(before, "parser", "Deadline: October 15.\n");
        var afterExtraction = new DocumentExtraction(after, "parser", "Deadline: October 30.\n");
        await extractions.SaveAsync(beforeExtraction, default);
        await extractions.SaveAsync(afterExtraction, default);
        var comparison = await new PostgresDocumentComparisonStore(factory).CompareAndSaveAsync(
            beforeExtraction.ExtractionId, afterExtraction.ExtractionId, default);

        var evidence = (await analysis.ReadEvidenceAsync(comparison.ComparisonId, default))!;

        Assert.Equal(revision.Id, evidence.ConfigurationRevision!.Id);
    }

    [Fact]
    public async Task EvidenceOfAnAttemptWithoutAJobHasNoConfigurationRevision()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");

        Assert.Null((await analysis.ReadEvidenceAsync(comparison.ComparisonId, default))!.ConfigurationRevision);
    }

    [Fact]
    public async Task AnAiDraftBecomesRevisionOneLinkedToItsRunAndOnlyPeopleRevisePublishOrDecide()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        var queued = Assert.Single(await analysis.GetByComparisonIdsAsync([comparison.ComparisonId], default));
        Assert.Equal(AnalysisState.Pending, queued.Status);
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "October 30")));

        var result = await Worker(model).ExecuteAsync(once: true);

        Assert.Equal((1, 0), (result.ProgressCount, result.Failures));
        var entry = await EntryAsync(comparison);
        Assert.Equal(AnalysisState.Succeeded, entry.Status);
        var review = (await reviews.GetAsync(entry.DraftId!, default))!;
        var revision = review.CurrentRevision;
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(ReviewAuthor.AnalysisSubject, revision.AuthorSubject);
        var citation = Assert.Single(revision.Citations);
        Assert.Equal(comparison.AfterExtractionId, citation.ExtractionId);
        Assert.Equal("Deadline: October 30.\n".IndexOf("October 30", StringComparison.Ordinal), citation.Start);

        var run = (await analysis.GetDraftRunAsync(entry.DraftId!, default))!;
        Assert.Equal(AnalysisRunOutcome.Drafted, run.Outcome);
        Assert.Equal((DocumentChangeDraftTask.Model, DocumentChangeDraftTask.PromptVersion, DocumentChangeDraftTask.TaskVersion),
            (run.Model, run.PromptVersion, run.TaskVersion));
        var evidence = (await analysis.ReadEvidenceAsync(comparison.ComparisonId, default))!;
        Assert.Equal(DocumentChangeDraftPrompt.Build(evidence, Catalog)!.InputHash, run.InputHash);
        Assert.Equal(model.Requests[0].InputHash, run.InputHash);
        Assert.Equal(DocumentChangeDraftPrompt.SerializeCatalog(Catalog), run.ContextJson);
        Assert.Equal(1_200, await ChargedTodayAsync());

        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), entry.DraftId!, 1, 1, ReviewDecisionKind.Approve,
            ReviewAuthor.AnalysisSubject, null, [], DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reviews.DecideAsync(ReviewAuthor.AnalysisSubject, decision, 1, 0,
            "key", new string('a', 64), [], [], default));

        var saved = await new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new SaveDocumentChangeDraftRequest(entry.DraftId!, 1, "Edited headline", revision.Summary, null, revision.Limits,
                revision.Institution, null, null, [], [], revision.Citations, "save-1"));
        Assert.Equal((2, Owner.Subject), (saved.RevisionNumber, saved.AuthorSubject));
        var approval = await new DecideDocumentChangeReview(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new DecideDocumentChangeReviewRequest(entry.DraftId!, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-1"));
        Assert.Equal((2, Owner.Subject), (approval.RevisionNumber, approval.ActorSubject));

        Assert.Equal(0, (await Worker(new FakeDocumentChangeDraftModel()).ExecuteAsync(once: true)).Failures);
        var conflict = await Assert.ThrowsAsync<DocumentChangeDraftExistsException>(() => new CreateDocumentChangeDraft(reviews)
            .ExecuteAsync(Owner, new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "start-late")));
        Assert.Equal(entry.DraftId, conflict.DraftId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAiChangeDateSurvivesAHumanSaveOnlyWhenThePersonChecksIt(bool changeDateChecked)
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Updated October 3, 2026. Deadline: October 30.\n");
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output("Headline",
            [("h1", "after", "Updated October 3, 2026.")], changeDate: new { date = "2026-10-03", citationIndex = 0, confidence = 0.9 }));
        await Worker(model).ExecuteAsync(once: true);
        var ai = (await reviews.GetAsync((await EntryAsync(comparison)).DraftId!, default))!.CurrentRevision;
        Assert.Equal(new DateOnly(2026, 10, 3), ai.ChangeDate);

        var saved = await new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new SaveDocumentChangeDraftRequest(ai.DraftId, 1, ai.Headline, ai.Summary, ai.Significance, ai.Limits, ai.Institution,
                ai.ChangeDate, ai.ChangeDateEvidence, [], [], ai.Citations, "save-date", changeDateChecked));

        Assert.Equal(changeDateChecked ? ai.ChangeDate : null, saved.ChangeDate);
        Assert.Equal(changeDateChecked ? ai.ChangeDateEvidence : null, saved.ChangeDateEvidence);
        Assert.Equal(ai.Citations.ToArray(), saved.Citations.ToArray());
    }

    [Fact]
    public async Task OnlyComparisonsWithATextChangeAreQueued()
    {
        var unchanged = await seeder.SaveAsync("Same text.\n", "Same text.\n");
        Assert.Empty(await analysis.GetByComparisonIdsAsync([unchanged.ComparisonId], default));
    }

    [Fact]
    public async Task ARefusalIsAVisibleFailedRunWithoutRetryOrDraft()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var model = new FakeDocumentChangeDraftModel().Reply(new AnalysisModelResponse("refusal", null, FakeDocumentChangeDraftModel.Usage(400, 20), null));

        await Worker(model).ExecuteAsync(once: true);

        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Failed, "refused", null), (entry.Status, entry.ErrorCode, entry.DraftId));
        Assert.Single(model.Requests);
        Assert.Equal(["Refused"], await RunOutcomesAsync(comparison));
        Assert.Equal(420, await ChargedTodayAsync());
    }

    [Fact]
    public async Task ACitationFailureRetriesOnceWithItsReasonsAndThenFailsVisibly()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        var bad = FakeDocumentChangeDraftModel.Output(("h1", "after", "October 31"));
        var model = new FakeDocumentChangeDraftModel().Draft(bad).Draft(bad);

        await Worker(model).ExecuteAsync(once: true);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("does not occur", model.Requests[1].UserPrompt, StringComparison.Ordinal);
        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Failed, "citationRejected", null), (entry.Status, entry.ErrorCode, entry.DraftId));
        Assert.Equal(["CitationRejected", "CitationRejected"], await RunOutcomesAsync(comparison));
        Assert.Equal(1, await ScalarAsync<long>($"""
            SELECT count(*) FROM document_change_analysis_runs second JOIN document_change_analysis_runs first
              ON second.previous_run_id = first.run_id WHERE first.comparison_id = '{comparison.ComparisonId}'
            """));
        Assert.Equal(2_400, await ChargedTodayAsync());
    }

    [Fact]
    public async Task ACorrectedRetryBecomesTheDraft()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "e")))
            .Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "October 30")));

        await Worker(model).ExecuteAsync(once: true);

        var entry = await EntryAsync(comparison);
        Assert.Equal(AnalysisState.Succeeded, entry.Status);
        Assert.NotNull(entry.DraftId);
        Assert.Equal(["CitationRejected", "Drafted"], await RunOutcomesAsync(comparison));
    }

    [Fact]
    public async Task AValidationRetryResumesAfterABudgetWaitWithThePreviousErrors()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        var first = DocumentChangeDraftPrompt.Build((await analysis.ReadEvidenceAsync(comparison.ComparisonId, default))!, Catalog)!;
        var tight = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "October 31")));

        await Worker(tight, new DocumentChangeDraftSettings(first.ReservedTokens + 1_300, 1))
            .ExecuteAsync(once: true);

        var waiting = await EntryAsync(comparison);
        Assert.Equal(AnalysisState.WaitingForBudget, waiting.Status);
        Assert.NotNull(waiting.RetryOfRunId);
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "October 30")));
        await Worker(model).ExecuteAsync(once: true);

        Assert.Contains("does not occur", Assert.Single(model.Requests).UserPrompt, StringComparison.Ordinal);
        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Succeeded, null), (entry.Status, entry.RetryOfRunId));
        Assert.Equal(waiting.RetryOfRunId, (await analysis.GetDraftRunAsync(entry.DraftId!, default))!.PreviousRunId);
    }

    [Fact]
    public async Task AShutdownDuringACallKeepsTheChargeAndCountsAnAttempt()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        using var shutdown = new CancellationTokenSource();
        var model = new FakeDocumentChangeDraftModel().Reply(async (_, token) =>
        {
            await shutdown.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });

        await Worker(model).ExecuteAsync(once: true, shutdown.Token);

        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Pending, 1), (entry.Status, entry.Attempts));
        Assert.Equal(["Interrupted"], await RunOutcomesAsync(comparison));
        Assert.Equal(model.Requests[0].ReservedTokens, await ChargedTodayAsync());
    }

    [Fact]
    public async Task TheClaimFailsAnEntryThatUsedAllItsAttemptsWithoutACall()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        await ScalarAsync<int>($"""
            UPDATE document_change_analysis SET status = 'RetryWaiting', attempts = {AnalysisPolicy.MaximumAttempts},
                error_code = 'rateLimited', retry_at = 0 WHERE comparison_id = '{comparison.ComparisonId}' RETURNING 0
            """);
        var model = new FakeDocumentChangeDraftModel();

        await Worker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Failed, "attemptsExhausted"), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task OnlyTheOwnerRequeuesFailedEntriesAndTheyStartAgain()
    {
        var failed = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var pending = await seeder.SaveAsync("Text C.\n", "Text D.\n");
        await ScalarAsync<int>($"""
            UPDATE document_change_analysis SET status = 'Failed', attempts = 3, error_code = 'refused'
            WHERE comparison_id = '{failed.ComparisonId}' RETURNING 0
            """);
        var requeue = new RequeueAnalyses<AnalysisRun>(analysis);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            requeue.ExecuteAsync(new ReviewActor("auth0|friend", ReviewRole.Reviewer), null));

        Assert.Equal(1, await requeue.ExecuteAsync(Owner, null));

        var entry = await EntryAsync(failed);
        Assert.Equal((AnalysisState.Pending, 0, null), (entry.Status, entry.Attempts, entry.ErrorCode));
        Assert.Equal(AnalysisState.Pending, (await EntryAsync(pending)).Status);
        Assert.Equal(0, await requeue.ExecuteAsync(Owner, pending.ComparisonId));
    }

    [Fact]
    public async Task SeveralCallsRunAtOnceUpToTheConcurrencyLimit()
    {
        for (var index = 0; index < 4; index++) await seeder.SaveAsync($"Item {index} old.\n", $"Item {index} new.\n");
        var running = 0;
        var peak = 0;
        var limitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FakeDocumentChangeDraftModel();
        for (var index = 0; index < 4; index++)
            model.Reply(async (request, token) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                if (now == 3) limitReached.TrySetResult();
                await limitReached.Task.WaitAsync(HangGuard, token);
                Interlocked.Decrement(ref running);
                var quote = request.UserPrompt.Contains("Item 0 new", StringComparison.Ordinal) ? "Item 0 new"
                    : request.UserPrompt.Contains("Item 1 new", StringComparison.Ordinal) ? "Item 1 new"
                    : request.UserPrompt.Contains("Item 2 new", StringComparison.Ordinal) ? "Item 2 new" : "Item 3 new";
                return new AnalysisModelResponse("end_turn", FakeDocumentChangeDraftModel.Output(("h1", "after", quote)),
                    FakeDocumentChangeDraftModel.Usage(900, 300), null);
            });

        await Worker(model, new DocumentChangeDraftSettings(10_000_000, concurrency: 3)).ExecuteAsync(once: true);

        Assert.Equal(3, peak);
        Assert.Equal(4, await ScalarAsync<long>("SELECT count(*) FROM document_change_drafts"));
        Assert.Equal(4, await ScalarAsync<long>("SELECT count(*) FROM document_change_drafts WHERE review_status = 'AwaitingReview'"));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current) current = Interlocked.CompareExchange(ref target, value, current) is var seen && seen == current ? value : seen;
    }

    [Fact]
    public async Task ARateLimitWaitsDurablyAndRefundsItsReservation()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var model = new FakeDocumentChangeDraftModel().Fail(AnalysisModelFailure.RateLimited);

        await Worker(model).ExecuteAsync(once: true);


        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.RetryWaiting, "rateLimited", 1), (entry.Status, entry.ErrorCode, entry.Attempts));
        Assert.InRange(entry.RetryAt!.Value - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(31));
        Assert.Equal(["RateLimited"], await RunOutcomesAsync(comparison));
        Assert.Equal(0, await ChargedTodayAsync());
    }

    [Fact]
    public async Task AConnectionFailureKeepsItsFullReservationCharged()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var model = new FakeDocumentChangeDraftModel().Fail(AnalysisModelFailure.ConnectionFailed);

        await Worker(model).ExecuteAsync(once: true);

        Assert.Equal(AnalysisState.RetryWaiting, (await EntryAsync(comparison)).Status);
        Assert.Equal(model.Requests[0].ReservedTokens, await ChargedTodayAsync());
    }

    [Fact]
    public async Task WorkOverTheDailyLimitWaitsUntilTheNextUtcDayWithoutACall()
    {
        var first = await seeder.SaveAsync("Alpha one.\n", "Alpha two.\n");
        var second = await seeder.SaveAsync("Beta one.\n", "Beta two.\n");
        var reservation = DocumentChangeDraftPrompt.Build((await analysis.ReadEvidenceAsync(first.ComparisonId, default))!, Catalog)!
            .ReservedTokens;
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "Alpha two")));

        await Worker(model, new DocumentChangeDraftSettings(reservation + 100, 1)).ExecuteAsync(once: true);

        Assert.Single(model.Requests);
        var waiting = await EntryAsync(second);
        Assert.Equal((AnalysisState.WaitingForBudget, "dailyTokenLimit"), (waiting.Status, waiting.ErrorCode));
        var tomorrow = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);
        Assert.Equal(tomorrow, waiting.RetryAt);
        Assert.Equal(1, await analysis.ReleaseBudgetWaitsAsync(default));
        Assert.Equal(AnalysisState.Pending, (await EntryAsync(second)).Status);
    }

    [Fact]
    public async Task ConcurrentReservationsNeverPassTheDailyLimit()
    {
        var claims = new List<AnalysisClaim>();
        for (var index = 0; index < 6; index++)
        {
            var comparison = await seeder.SaveAsync($"Item {index} old.\n", $"Item {index} new.\n");
            claims.Add((await analysis.TryClaimAsync(comparison.ComparisonId, TimeSpan.FromMinutes(1), default))!);
        }

        var results = await Task.WhenAll(claims.Select(claim =>
            analysis.ReserveAsync(claim, Guid.NewGuid().ToString("N"), new string('f', 64), 1_000, 3_500, default)));

        Assert.Equal(3, results.Count(result => result == AnalysisReservation.Reserved));
        Assert.Equal(3, results.Count(result => result == AnalysisReservation.WaitingForBudget));
        Assert.Equal(3_000, await ChargedTodayAsync());
    }

    [Fact]
    public async Task AnExpiredLeaseIsRecoveredAsAnInterruptedRunAndTheStaleOwnerIsFenced()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var stale = (await analysis.TryClaimAsync(comparison.ComparisonId, TimeSpan.FromMinutes(1), default))!;
        var runId = Guid.NewGuid().ToString("N");
        Assert.Equal(AnalysisReservation.Reserved,
            await analysis.ReserveAsync(stale, runId, new string('e', 64), 5_000, 100_000, default));
        await ExecuteAsync(connectionString, $"UPDATE document_change_analysis SET lease_expires_at = 0 WHERE comparison_id = '{comparison.ComparisonId}'");

        var current = await analysis.TryClaimAsync(comparison.ComparisonId, TimeSpan.FromMinutes(1), default);

        Assert.NotNull(current);
        Assert.Equal(1, current.Record.Attempts);
        Assert.Equal(["Interrupted"], await RunOutcomesAsync(comparison));
        Assert.Equal(5_000, await ScalarAsync<long>($"SELECT charged_tokens FROM document_change_analysis_runs WHERE run_id = '{runId}'"));
        Assert.Equal(5_000, await ChargedTodayAsync());
        var late = Run(stale.Record.SubjectId, runId, AnalysisRunOutcome.RateLimited);
        Assert.False(await analysis.CheckpointAsync(AnalysisCheckpoint.From(stale, AnalysisState.RetryWaiting,
            "rateLimited", TimeSpan.FromSeconds(30)), late, default));
        Assert.Equal(AnalysisReservation.LeaseLost,
            await analysis.ReserveAsync(stale, Guid.NewGuid().ToString("N"), new string('e', 64), 5_000, 100_000, default));
        Assert.Single(await RunOutcomesAsync(comparison));
    }

    [Fact]
    public async Task AHumanDraftBeforeTheCallCostsNothing()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        _ = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "k"));
        var model = new FakeDocumentChangeDraftModel();

        await Worker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Succeeded, "humanDraftExists"), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task AHumanDraftStartedDuringTheCallWinsAndTheAiDraftIsDiscarded()
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        string? humanDraft = null;
        var model = new FakeDocumentChangeDraftModel().Reply(async (_, _) =>
        {
            humanDraft = (await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner,
                new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "during"))).DraftId;
            return new AnalysisModelResponse("end_turn", FakeDocumentChangeDraftModel.Output(("h1", "after", "October 30")),
                FakeDocumentChangeDraftModel.Usage(900, 300), null);
        });

        await Worker(model).ExecuteAsync(once: true);

        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Succeeded, "draftDiscarded", null), (entry.Status, entry.ErrorCode, entry.DraftId));
        Assert.Equal(["DraftDiscarded"], await RunOutcomesAsync(comparison));
        Assert.Equal(1, await ScalarAsync<long>(
            $"SELECT count(*) FROM document_change_drafts WHERE comparison_id = '{comparison.ComparisonId}'"));
        Assert.Equal(Owner.Subject, (await reviews.GetAsync(humanDraft!, default))!.CurrentRevision.AuthorSubject);
    }

    [Fact]
    public async Task AComparisonOverTheInputBoundIsBlockedWithoutACall()
    {
        var before = string.Concat(Enumerable.Range(0, 65).Select(index => $"line {index}\nsame {index}\n"));
        var after = string.Concat(Enumerable.Range(0, 65).Select(index => $"edit {index}\nsame {index}\n"));
        var comparison = await seeder.SaveAsync(before, after);
        var model = new FakeDocumentChangeDraftModel();

        await Worker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Blocked, "inputLimitExceeded"), (entry.Status, entry.ErrorCode));
    }

    [Fact]
    public async Task ReplayingASettlementAfterItCommittedChangesNothing()
    {
        var comparison = await seeder.SaveAsync("Text A.\n", "Text B.\n");
        var claim = (await analysis.TryClaimAsync(comparison.ComparisonId, TimeSpan.FromMinutes(1), default))!;
        var runId = Guid.NewGuid().ToString("N");
        await analysis.ReserveAsync(claim, runId, new string('e', 64), 5_000, 100_000, default);
        var run = Run(comparison.ComparisonId, runId, AnalysisRunOutcome.Refused);

        var fail = AnalysisCheckpoint.From(claim, AnalysisState.Failed, "refused");
        Assert.True(await analysis.CheckpointAsync(fail, run, default));
        Assert.False(await analysis.CheckpointAsync(fail, run, default));

        Assert.Equal(["Refused"], await RunOutcomesAsync(comparison));
        Assert.Equal(0, await ChargedTodayAsync());
    }

    private AnalysisWorker<AnalysisRun> Worker(FakeDocumentChangeDraftModel model, DocumentChangeDraftSettings? settings = null) =>
        AnalysisWorkers.Drafting(analysis, new PostgresAnalysisStatusStore(factory), model,
            settings ?? new DocumentChangeDraftSettings(10_000_000, concurrency: 1), [], TimeProvider.System);

    private static AnalysisRun Run(string comparisonId, string runId, AnalysisRunOutcome outcome) => new(runId, comparisonId,
        DocumentChangeDraftTask.Task, DocumentChangeDraftTask.TaskVersion, DocumentChangeDraftTask.Model,
        DocumentChangeDraftTask.PromptVersion, DocumentChangeDraftTask.SchemaVersion, new string('e', 64), null, outcome,
        AnalysisTokenUsage.None, 0, null, null, null, ImmutableArray<string>.Empty, "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private async Task<DocumentChangeAnalysisRecord> EntryAsync(DocumentComparison comparison) =>
        Assert.Single(await analysis.GetByComparisonIdsAsync([comparison.ComparisonId], default));

    private async Task<string[]> RunOutcomesAsync(DocumentComparison comparison)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT outcome FROM document_change_analysis_runs WHERE comparison_id = @id ORDER BY started_at_utc_ticks, run_id", connection);
        command.Parameters.AddWithValue("id", comparison.ComparisonId);
        await using var reader = await command.ExecuteReaderAsync();
        var outcomes = new List<string>();
        while (await reader.ReadAsync()) outcomes.Add(reader.GetString(0));
        return [.. outcomes];
    }

    private Task<long> ChargedTodayAsync() => ScalarAsync<long>(
        "SELECT COALESCE(sum(charged_tokens), 0)::bigint FROM document_change_analysis_budget");

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
