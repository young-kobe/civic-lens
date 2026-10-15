using System.Data.Common;
using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Quote = "a rent cap for tenants";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-statement-review-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresStatementAnalysisStore gates = null!;
    private PostgresStatementDraftStore drafts = null!;
    private PostgresStatementReviewStore reviews = null!;
    private StatementCandidateSeeder seeder = null!;
    private int candidateCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        gates = new PostgresStatementAnalysisStore(factory);
        drafts = new PostgresStatementDraftStore(factory);
        reviews = new PostgresStatementReviewStore(factory);
        seeder = new StatementCandidateSeeder(attempts, new PostgresDocumentExtractionStore(factory), connectionString, captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    private CreateStatementDraft Create => new(reviews);
    private SaveStatementDraft Save => new(reviews, Reader(StatementGateFixtures.Configuration()));
    private DecideStatementReview Decide => new(reviews, Reader(StatementGateFixtures.Configuration()));

    private StatementReviewContextReader Reader(CivicLens.Application.Issues.IssueConfiguration issues) => new(drafts, new StatementSelectionResolver(issues));

    [Fact]
    public async Task CreateStartsRevisionOneReplaysAndRefusesASecondDraft()
    {
        var candidate = await CandidateAsync();
        var other = await CandidateAsync();
        var request = new CreateStatementDraftRequest(candidate, "create-key");

        var first = await Create.ExecuteAsync(Owner, request);
        var replay = await Create.ExecuteAsync(Owner, request);

        Assert.Equal(first.CreatedAtUtc, replay.CreatedAtUtc);
        await Assert.ThrowsAsync<ArgumentException>(() => Create.ExecuteAsync(Owner, request with { CandidateId = other }));
        var exists = await Assert.ThrowsAsync<StatementDraftExistsException>(() => Create.ExecuteAsync(Owner, request with { IdempotencyKey = "again" }));
        Assert.Equal(candidate, exists.CandidateId);
        Assert.Equal(("AwaitingReview", 1L, 0L, 1L), await DraftStateAsync(candidate));
        Assert.Equal(("Person", Owner.Subject), await FirstRevisionAuthorAsync(candidate));
    }

    [Fact]
    public async Task CreateAddsRevisionOneToAnOverCapDraftRowAndNeedsAnExistingCandidate()
    {
        var candidate = await CandidateAsync();
        await InsertRevisionLessDraftAsync(candidate);

        await Create.ExecuteAsync(Owner, new(candidate, "create-key"));

        Assert.Equal(("AwaitingReview", 1L, 0L, 1L), await DraftStateAsync(candidate));
        await Assert.ThrowsAsync<ArgumentException>(() => Create.ExecuteAsync(Owner, new(new string('a', 64), "unknown")));
    }

    [Fact]
    public async Task AStaleSaveConflictsAndAReplayReturnsTheOriginalRevisionOnlyForTheSamePayload()
    {
        var candidate = await StartedAsync();
        var request = await SaveRequestAsync(candidate, 1, "save-a");

        var first = await Save.ExecuteAsync(Owner, request);
        var replay = await Save.ExecuteAsync(Owner, request);

        Assert.Equal((2, first.CreatedAtUtc), (replay.RevisionNumber, replay.CreatedAtUtc));
        await Assert.ThrowsAsync<ReviewConflictException>(() => Save.ExecuteAsync(Owner, request with { IdempotencyKey = "save-b" }));
        await Assert.ThrowsAsync<ArgumentException>(() => Save.ExecuteAsync(Owner, request with { Headline = "Different" }));
        Assert.Equal(2L, await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task ConcurrentSavesAppendOnlyOneRevision()
    {
        var candidate = await StartedAsync();
        var request = await SaveRequestAsync(candidate, 1, "save-a");

        var outcomes = await Task.WhenAll(Observe(Save.ExecuteAsync(Owner, request)),
            Observe(Save.ExecuteAsync(Owner, request with { Headline = "Competing", IdempotencyKey = "save-b" })));

        Assert.Single(outcomes, outcome => outcome is StatementDraftRevision);
        Assert.Single(outcomes, outcome => outcome is ReviewConflictException);
        Assert.Equal(2L, await ScalarAsync($"SELECT current_revision_number FROM statement_drafts WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task GetReadsTheAdmittingRevisionWithNoExtraQueryForAnUndraftedCandidate()
    {
        var drafted = await ApprovableAsync();
        var undrafted = await CandidateAsync();
        var counter = new ReadCommandCounter();
        var counted = new PostgresStatementReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));

        await counted.GetAsync(drafted, default);
        counter.ReadCount = 0;
        var read = await counted.GetAsync(drafted, default);
        var draftedReads = counter.ReadCount;
        counter.ReadCount = 0;
        await counted.GetAsync(undrafted, default);

        Assert.NotNull(read!.ConfigurationRevision);
        Assert.InRange(counter.ReadCount, 1, draftedReads);
        Assert.InRange(draftedReads, 1, 9);
    }

    [Fact]
    public async Task GetReturnsTheAllowedSelectionsTheWriteHandlersWouldUse()
    {
        var candidate = await ApprovableAsync();
        var get = new GetStatementReview(reviews, new StatementSelectionResolver(StatementGateFixtures.Configuration()));

        var detail = (await get.ExecuteAsync(Owner, candidate))!;
        var context = await Reader(StatementGateFixtures.Configuration()).ReadAsync(candidate, default);

        Assert.Equal(context.Allowed.OfficialIds, detail.Allowed.OfficialIds);
        Assert.Equal(context.Allowed.IssueIds, detail.Allowed.IssueIds);
        Assert.Equal(context.Allowed.PolicyAreaIds, detail.Allowed.PolicyAreaIds);
        Assert.Equal(context.Officials.Select(official => official.Id), detail.Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task StoredStatusEqualsThePolicyStatusAfterEveryKindOfWrite()
    {
        var candidate = await StartedAsync();
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.AwaitingReview);

        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 1, "save-1"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.AwaitingReview);

        var concern = await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.RequestChanges, "Context missing.", [], "concern"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.ChangesRequested);

        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 2, "save-2"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.AwaitingReviewWithConcerns);

        await Decide.ExecuteAsync(Owner, new(candidate, 3, 1, ReviewDecisionKind.Approve, "Resolved.", [concern.DecisionId], "approve"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.Approved);

        await Decide.ExecuteAsync(Owner, new(candidate, 3, 2, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.ApprovalWithdrawn);

        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 3, "save-3"));
        await AssertStoredStatusMatchesPolicyAsync(candidate, ReviewStatus.AwaitingReviewWithConcerns);
    }

    [Fact]
    public async Task DecisionReplayReturnsTheOriginalDecisionAndAStaleStateConflicts()
    {
        var candidate = await ApprovableAsync();
        var request = new DecideStatementReviewRequest(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve");

        var first = await Decide.ExecuteAsync(Owner, request);
        var replay = await Decide.ExecuteAsync(Owner, request);

        Assert.Equal(first.DecisionId, replay.DecisionId);
        await Assert.ThrowsAsync<ArgumentException>(() => Decide.ExecuteAsync(Owner, request with { Note = "Different payload." }));
        await Assert.ThrowsAsync<ReviewConflictException>(() => Decide.ExecuteAsync(Owner,
            request with { Kind = ReviewDecisionKind.RequestChanges, Note = "Late.", IdempotencyKey = "late" }));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_review_decisions WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task ApprovalNeedsEveryApprovalFieldAndRecordsNothingWhenItFails()
    {
        var candidate = await StartedAsync();
        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 1, "save") with { SpeakerOfficialIds = [] });

        await Assert.ThrowsAsync<ArgumentException>(() => Decide.ExecuteAsync(Owner,
            new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve")));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_review_decisions"));
        Assert.Equal(("AwaitingReview", 2L, 0L, 2L), await DraftStateAsync(candidate));
    }

    [Fact]
    public async Task ApprovalRefusesASpeakerOrAnIssueThatIsNoLongerAllowed()
    {
        var candidate = await ApprovableAsync();
        var approve = new DecideStatementReviewRequest(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve");
        var withoutRentIssue = StatementGateFixtures.Configuration() with
        {
            Issues = [.. StatementGateFixtures.Configuration().Issues.Where(issue => issue.Id != RentIssue)]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => new DecideStatementReview(reviews, Reader(withoutRentIssue)).ExecuteAsync(Owner, approve));

        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), candidate, 2, 1, ReviewDecisionKind.Approve, Owner.Subject, null, [],
            DateTimeOffset.UtcNow);
        var withoutWarren = new StatementAllowedSelections([Markey], [RentIssue], [HousingArea]);
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.DecideAsync(Owner.Subject, decision, 2, 0, withoutWarren,
            "direct", new string('a', 64), default));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_review_decisions"));
    }

    [Fact]
    public async Task AnAiDateCannotBeApprovedAndSurvivesAPersonsSaveOnlyWhenTheyTickThatTheyCheckedIt()
    {
        var ticked = await AiDraftedCandidateAsync();
        var unticked = await AiDraftedCandidateAsync();
        var detail = (await reviews.GetAsync(ticked, default))!;
        Assert.NotNull(detail.Review.CurrentRevision!.StatementDate);
        Assert.NotNull(detail.AnalysisRunId);

        await Assert.ThrowsAsync<ArgumentException>(() => Decide.ExecuteAsync(Owner,
            new(ticked, 1, 0, ReviewDecisionKind.Approve, null, [], "approve-ai")));
        var kept = await Save.ExecuteAsync(Owner, await SaveFromAiAsync(ticked, statementDateChecked: true));
        var dropped = await Save.ExecuteAsync(Owner, await SaveFromAiAsync(unticked, statementDateChecked: false));

        Assert.Equal(detail.Review.CurrentRevision.StatementDate, kept.StatementDate);
        Assert.NotNull(kept.StatementDateEvidence);
        Assert.Null(dropped.StatementDate);
        Assert.Null(dropped.StatementDateEvidence);
        await Decide.ExecuteAsync(Owner, new(ticked, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-ticked"));
        await Decide.ExecuteAsync(Owner, new(unticked, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-unticked"));
        Assert.Equal(ReviewStatus.Approved, (await reviews.GetAsync(ticked, default))!.Review.CurrentStatus);
    }

    [Fact]
    public async Task ASpanOutsideTheExtractionOrOnAnotherExtractionIsRefusedAndNothingIsWritten()
    {
        var candidate = await StartedAsync();
        var request = await SaveRequestAsync(candidate, 1, "save");
        var extraction = request.Quotations[0].ExtractionId;

        await Assert.ThrowsAsync<ArgumentException>(() => Save.ExecuteAsync(Owner,
            request with { Quotations = [new(extraction, Article.Length + 50, 10)] }));
        await Assert.ThrowsAsync<ArgumentException>(() => Save.ExecuteAsync(Owner,
            request with { Quotations = [new(new string('b', 64), 0, 10)] }));

        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task AHumanCreateAndAnAiSettlementRacingForTheSameCandidateLeaveExactlyOneFirstRevision()
    {
        var candidate = await PassedCandidateAsync();
        var model = new FakeStatementDraftingModel().Draft(Output());

        var outcomes = await Task.WhenAll(Observe(Create.ExecuteAsync(Owner, new(candidate, "create"))),
            Task.Run(async () =>
            {
                await DraftingWorker(model).ExecuteAsync(once: true);
                return (object)"settled";
            }));

        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));
        var humanWon = await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}' AND author_kind = 'Person'") == 1;
        Assert.Equal(humanWon, outcomes[0] is StatementDraftRevision);
        if (humanWon) Assert.Contains(await QueueCodeAsync(candidate), new[] { "humanDraftExists", "draftDiscarded" });
        else Assert.Null(await QueueCodeAsync(candidate));
        if (!humanWon) Assert.IsType<StatementDraftExistsException>(outcomes[0]);
    }

    [Fact]
    public async Task StatementWritesWaitForTheStatementLockAndIgnoreTheDocumentChangeLock()
    {
        var candidate = await CandidateAsync();
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-document-change-review");
        await Create.ExecuteAsync(Owner, new(candidate, "unblocked"));
        await holder.DisposeAsync();

        var other = await CandidateAsync();
        await using var statementHolder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-statement-review");
        var blocked = Create.ExecuteAsync(Owner, new(other, "blocked"));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-statement-review");
        Assert.False(blocked.IsCompleted);
        await statementHolder.DisposeAsync();
        await blocked;
    }

    [Fact]
    public async Task AiSettlementWaitsForTheStatementLock()
    {
        var candidate = await PassedCandidateAsync();
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-statement-review");

        var settling = DraftingWorker(new FakeStatementDraftingModel().Draft(Output())).ExecuteAsync(once: true);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-statement-review");
        Assert.False(settling.IsCompleted);
        await holder.DisposeAsync();
        await settling;

        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task ListPagesByCreationTimeAndFiltersByTheStoredStatus()
    {
        var overCap = await CandidateAsync();
        var awaiting = await StartedAsync();
        var requested = await ApprovableAsync();
        var approvedOlder = await ApprovableAsync();
        var approvedNewest = await ApprovableAsync();
        await InsertRevisionLessDraftAsync(overCap);
        await Decide.ExecuteAsync(Owner, new(requested, 2, 0, ReviewDecisionKind.RequestChanges, "Needs sources.", [], "request"));
        foreach (var candidate in new[] { approvedOlder, approvedNewest })
            await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-" + candidate[..4]));
        var order = new[] { overCap, awaiting, requested, approvedOlder, approvedNewest };
        for (var index = 0; index < order.Length; index++)
            await ExecuteAsync(connectionString, $"UPDATE statement_drafts SET created_at_utc_ticks = {9_000 + index} WHERE candidate_id = '{order[index]}'");
        var list = new ListStatementReviews(reviews);

        async Task<PageWalk> Walk(DraftStatusFilter filter) => await PageWalk.RunAsync(async cursor =>
        {
            var page = await list.ExecuteAsync(Owner, cursor?.Encode(), 2, filter);
            return ([.. page.Items.Select(item => item.CandidateId)], page.NewerCursor, page.OlderCursor);
        });

        (await Walk(DraftStatusFilter.All)).AssertExact([approvedNewest, approvedOlder, requested, awaiting, overCap]);
        (await Walk(DraftStatusFilter.NeedsAction)).AssertExact([requested, awaiting, overCap]);
        (await Walk(DraftStatusFilter.Approved)).AssertExact([approvedNewest, approvedOlder]);
        var everything = (await list.ExecuteAsync(Owner, null, 10)).Items.ToDictionary(item => item.CandidateId);
        Assert.Null(everything[overCap].CurrentRevision);
        Assert.Equal(ReviewStatus.NotDrafted, everything[overCap].CurrentStatus);
        Assert.Equal((ReviewStatus.ChangesRequested, 1, 1), (everything[requested].CurrentStatus, everything[requested].ReviewStateVersion,
            everything[requested].UnresolvedConcernCount));
        Assert.Equal(2, everything[approvedNewest].CurrentRevision!.RevisionNumber);
        Assert.Equal("source", everything[approvedNewest].SourceId);
    }

    [Fact]
    public async Task TheNotInReviewListNamesEachCandidateWithoutADraftRevisionAndItsReason()
    {
        var overCap = await CandidateAsync();
        var skipped = await CandidateAsync();
        var gateFailed = await CandidateAsync();
        var draftBlocked = await CandidateAsync();
        var pending = await CandidateAsync();
        var queued = await CandidateAsync();
        var humanDraft = await StartedAsync();
        await InsertRevisionLessDraftAsync(overCap);
        await SetQueueAsync(skipped, "gate", "Succeeded", "no-tracked-issue");
        await SetQueueAsync(humanDraft, "gate", "Succeeded", "no-tracked-issue");
        await SetQueueAsync(gateFailed, "gate", "Failed", "providerUnavailable");
        await SetQueueAsync(draftBlocked, "gate", "Succeeded", "passed");
        await SetQueueAsync(draftBlocked, StatementDraftTask.Task, "Blocked", "speakersUnavailable");
        await SetQueueAsync(queued, "gate", "Succeeded", "passed");
        await SetQueueAsync(queued, StatementDraftTask.Task, "Pending", null);
        var order = new[] { overCap, skipped, gateFailed, draftBlocked, pending, queued, humanDraft };
        for (var index = 0; index < order.Length; index++)
            await ExecuteAsync(connectionString, $"UPDATE statement_candidates SET created_at_utc_ticks = {9_000 + index} WHERE candidate_id = '{order[index]}'");
        var list = new ListStatementsNotInReview(reviews);

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await list.ExecuteAsync(Owner, cursor?.Encode(), 2);
            return ([.. page.Items.Select(item => item.CandidateId)], page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact([draftBlocked, gateFailed, skipped, overCap]);
        var reasons = (await list.ExecuteAsync(Owner, null, 10)).Items.ToDictionary(item => item.CandidateId, item => (item.Stage, item.ReasonCode));
        Assert.Equal(("gate", "no-tracked-issue"), reasons[skipped]);
        Assert.Equal(("gate", "providerUnavailable"), reasons[gateFailed]);
        Assert.Equal((StatementDraftTask.Task, "speakersUnavailable"), reasons[draftBlocked]);
        Assert.Equal((StatementDraftTask.Task, "over-byte-cap"), reasons[overCap]);
    }

    [Fact]
    public async Task GetReturnsTheAggregateWithItsEvidenceAndNotDraftedCandidatesAndNothingForAnUnknownOne()
    {
        var candidate = await ApprovableAsync();
        var undrafted = await CandidateAsync();
        var get = new GetStatementReview(reviews, new StatementSelectionResolver(StatementGateFixtures.Configuration()));

        var detail = (await get.ExecuteAsync(Owner, candidate))!;
        var notDrafted = (await get.ExecuteAsync(Owner, undrafted))!;

        Assert.Equal((2, 1, 0, ReviewStatus.AwaitingReview), (detail.Review.CurrentRevision!.RevisionNumber, detail.Review.Revisions.Length,
            detail.Review.ReviewStateVersion, detail.Review.CurrentStatus));
        Assert.Contains(Quote, detail.Extraction.Text);
        Assert.Equal("source", detail.SourceId);
        Assert.Null(detail.AnalysisRunId);
        Assert.StartsWith("https://example.test/pages/", detail.Url);
        Assert.Null(notDrafted.Review.CurrentRevision);
        Assert.Equal((0, ReviewStatus.NotDrafted), (notDrafted.Review.ReviewStateVersion, notDrafted.Review.CurrentStatus));
        Assert.Null(await get.ExecuteAsync(Owner, new string('a', 64)));
    }

    private async Task<string> CandidateAsync() =>
        await seeder.SeedAsync($"{Article} Release {++candidateCount}.", $"https://example.test/pages/{candidateCount}");

    private async Task<string> StartedAsync()
    {
        var candidate = await CandidateAsync();
        await Create.ExecuteAsync(Owner, new(candidate, "create-" + candidate[..8]));
        return candidate;
    }

    private async Task<string> ApprovableAsync()
    {
        var candidate = await StartedAsync();
        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 1, "save-" + candidate[..8]));
        return candidate;
    }

    private async Task<SaveStatementDraftRequest> SaveRequestAsync(string candidate, int expectedRevision, string key)
    {
        var extraction = (await drafts.ReadEvidenceAsync(candidate, default))!.Article.Extraction;
        var span = new StatementSpan(extraction.ExtractionId, extraction.Text.IndexOf(Quote, StringComparison.Ordinal), Quote.Length);
        return new(candidate, expectedRevision, "Warren announces a rent cap", "The senator announced a rent cap for tenants.", null, null,
            [Warren], [], [RentIssue], [HousingArea], [span], key);
    }

    private async Task<SaveStatementDraftRequest> SaveFromAiAsync(string candidate, bool statementDateChecked)
    {
        var proposed = (await reviews.GetAsync(candidate, default))!.Review.CurrentRevision!;
        return new(candidate, 1, proposed.Headline, proposed.Summary, proposed.StatementDate, proposed.StatementDateEvidence,
            proposed.SpeakerOfficialIds, proposed.OtherSpeakers, proposed.IssueIds, proposed.PolicyAreaIds,
            [.. proposed.Quotations.Select(quotation => quotation.Span)], "save-" + candidate[..8], statementDateChecked);
    }

    private async Task<string> PassedCandidateAsync()
    {
        var candidate = await CandidateAsync();
        await StatementAiDrafting.PassGateAsync(gates);
        return candidate;
    }

    private async Task<string> AiDraftedCandidateAsync()
    {
        var candidate = await PassedCandidateAsync();
        await StatementAiDrafting.DraftAsync(drafts, Output(date: ("2026-10-03", "October 3, 2026")));
        return candidate;
    }

    private AnalysisWorker<StatementDraftRun> DraftingWorker(FakeStatementDraftingModel model) =>
        AnalysisWorkers.StatementDrafting(drafts, new FakeAnalysisStatusStore(), model, new StatementDraftSettings(10_000_000, concurrency: 1),
            TimeProvider.System);

    private static async Task<object> Observe<T>(Task<T> task)
    {
        try
        {
            return (await task)!;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task AssertStoredStatusMatchesPolicyAsync(string candidate, ReviewStatus expected)
    {
        var review = (await reviews.GetAsync(candidate, default))!.Review;
        var policy = ReviewPolicy.GetCurrentStatus(review.CurrentRevision!.RevisionNumber, review.Decisions);
        Assert.Equal(expected, policy);
        Assert.Equal(policy.ToString(), await TextAsync($"SELECT review_status FROM statement_drafts WHERE candidate_id = '{candidate}'"));
        Assert.Equal(policy, review.CurrentStatus);
    }

    private Task InsertRevisionLessDraftAsync(string candidate) => ExecuteAsync(connectionString,
        $"INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks) VALUES ('{candidate}', 0, 0, 'NotDrafted', 0)");

    private Task SetQueueAsync(string candidate, string task, string status, string? code) => ExecuteAsync(connectionString, $"""
        INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, error_code)
        VALUES ('{candidate}', '{task}', '{status}', 0, 0, 0, 0, {(code is null ? "NULL" : $"'{code}'")})
        ON CONFLICT (candidate_id, task) DO UPDATE SET status = EXCLUDED.status, error_code = EXCLUDED.error_code
        """);

    private async Task<(string Status, long Revision, long Version, long Revisions)> DraftStateAsync(string candidate) =>
        (await TextAsync($"SELECT review_status FROM statement_drafts WHERE candidate_id = '{candidate}'"),
            await ScalarAsync($"SELECT current_revision_number FROM statement_drafts WHERE candidate_id = '{candidate}'"),
            await ScalarAsync($"SELECT review_state_version FROM statement_drafts WHERE candidate_id = '{candidate}'"),
            await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));

    private async Task<(string Kind, string Subject)> FirstRevisionAuthorAsync(string candidate) =>
        (await TextAsync($"SELECT author_kind FROM statement_revisions WHERE candidate_id = '{candidate}' AND revision_number = 1"),
            await TextAsync($"SELECT author_subject FROM statement_revisions WHERE candidate_id = '{candidate}' AND revision_number = 1"));

    private async Task<string?> QueueCodeAsync(string candidate)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT error_code FROM statement_analysis WHERE candidate_id = '{candidate}' AND task = '{StatementDraftTask.Task}'", connection);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<string> TextAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        public int ReadCount { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult(result);
        }
    }
}
