using System.Collections.Immutable;
using System.Data.Common;
using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Core.Briefs;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
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
    private PostgresStatementGateStore gates = null!;
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
        gates = new PostgresStatementGateStore(factory);
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

    private RelationshipWorldSeeder Relationships => new(connectionString);

    private async Task<long> CountRowsAsync(string table) => long.Parse((await Relationships.ReadAsync($"SELECT count(*) FROM {table}")).Single()[0]);

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
    public async Task ApprovingAStatementQueuesItsRelationshipPairsInTheSameTransactionSoTheRelationshipWorkerWakes()
    {
        var candidate = await ApprovableAsync();
        await new RelationshipWorldSeeder(connectionString).ActionAsync(RelationshipFixtures.Action(title: Quote));
        await using var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresRelationshipDraftStore.Table,
            RelationshipDraftTask.Task);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waiting = wakeup.WaitAsync(timeout.Token);
        Assert.Equal(0, await CountRowsAsync("relationship_analysis"));

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));

        await waiting;
        Assert.Equal([["Pending", candidate, "Action"]], await Relationships.ReadAsync(
            "SELECT status, statement_candidate_id, target_kind FROM relationship_analysis"));
    }

    [Fact]
    public async Task ADecisionWakesAnIdleRelationshipWorkerSoANewApprovalIsDraftedWithoutAnyOtherEvent()
    {
        var candidate = await ApprovableAsync();
        await Relationships.ActionAsync(RelationshipFixtures.Action(title: Quote));
        var relationshipStore = new PostgresRelationshipDraftStore(factory);
        var model = new FakeRelationshipDraftModel();
        await using var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresRelationshipDraftStore.Table,
            RelationshipDraftTask.Task);
        var worker = new AnalysisWorker<RelationshipDraftRun>(new RelationshipDraftStep(relationshipStore, model,
            new RelationshipDraftSettings(10_000_000, concurrency: 1), RelationshipFixtures.Officials),
            new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task), TimeProvider.System, wakeup);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var running = worker.ExecuteAsync(once: false, stop.Token);
        await Task.Delay(1500);
        model.Draft(RelationshipFixtures.Output(quote: Quote, factId: "F2"));

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        while (model.Requests.Count == 0 && !stop.IsCancellationRequested) await Task.Delay(100);
        await Task.Delay(500);
        await stop.CancelAsync();
        await running;

        Assert.Single(model.Requests);
        Assert.Equal(1, await CountRowsAsync("relationship_revisions"));
    }

    [Fact]
    public async Task ReApprovingAStatementRequeuesAPairThatBlockedWhileItWasNotApprovedAndAChangeRequestNeverFlagsIt()
    {
        var candidate = await ApprovableAsync();
        await Relationships.ActionAsync(RelationshipFixtures.Action(title: Quote));
        await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        var pair = (await Relationships.ReadAsync("SELECT pair_id FROM relationship_analysis")).Single()[0];
        await Relationships.ExecuteAsync("""
            UPDATE relationship_analysis SET status = 'Blocked', error_code = 'sideNotApproved';
            SET session_replication_role = replica;
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
                created_at_utc_ticks, last_revised_at_utc_ticks) VALUES (@id, 1, 0, 'AwaitingReview', false, 1, 1)
            """, ("id", pair));
        var changes = await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.RequestChanges, "Name the bill.", [], "changes"));
        Assert.Equal([["Blocked", "False"]], await Relationships.ReadAsync(
            "SELECT a.status, d.no_longer_matches FROM relationship_analysis a JOIN relationship_drafts d ON d.record_id = a.pair_id"));

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 2, ReviewDecisionKind.Approve, "Fixed.", [changes.DecisionId], "approve-again"));

        Assert.Equal([["Pending"]], await Relationships.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task ADecisionLocksOnlyTheRelationshipRowsOfItsOwnRecordSoAnUnrelatedPairRowDoesNotBlockIt()
    {
        var candidate = await ApprovableAsync();
        await Relationships.ActionAsync(RelationshipFixtures.Action(title: Quote));
        await Relationships.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO relationship_analysis (pair_id, task, statement_candidate_id, target_kind, target_record_id, status, fence, attempts,
                reserved_tokens, created_at_utc_ticks)
            VALUES (repeat('e', 64), 'relationship-draft', repeat('f', 64), 'Action', repeat('9', 64), 'Pending', 0, 0, 0, 1)
            """);
        await using var holder = new Npgsql.NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await new Npgsql.NpgsqlCommand("SELECT 1 FROM relationship_analysis WHERE pair_id = repeat('e', 64) FOR UPDATE", holder, transaction)
            .ExecuteNonQueryAsync();

        var decision = Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));

        Assert.Same(decision, await Task.WhenAny(decision, Task.Delay(TimeSpan.FromSeconds(20))));
        await decision;
        Assert.Equal(2L, await CountRowsAsync("relationship_analysis"));
    }

    [Fact]
    public async Task ADecisionThatWaitsForTheRelationshipKeyHasNotReachedTheBriefKeyYetSoTheBriefHookStaysLast()
    {
        var candidate = await ApprovableAsync();
        await Relationships.ActionAsync(RelationshipFixtures.Action(title: Quote));
        await using var holder = new Npgsql.NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await AdvisoryAsync(holder, transaction, "civic-lens-relationship-review");

        var decision = Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        await Task.Delay(1500);

        Assert.False(decision.IsCompleted);
        await using var probe = new Npgsql.NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        await using var probeTransaction = await probe.BeginTransactionAsync();
        Assert.True((bool)(await new Npgsql.NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended('civic-lens-brief-review', 0))", probe,
            probeTransaction).ExecuteScalarAsync())!);
        await probeTransaction.RollbackAsync();
        await transaction.CommitAsync();
        await decision;
    }

    [Fact]
    public async Task ADecisionThatLeavesTheRecordNotApprovedTakesNoRelationshipLock()
    {
        var candidate = await ApprovableAsync();
        await using var holder = new Npgsql.NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await AdvisoryAsync(holder, transaction, "civic-lens-relationship-review");

        var decision = Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.RequestChanges, "Name the bill.", [], "changes"));

        Assert.Same(decision, await Task.WhenAny(decision, Task.Delay(TimeSpan.FromSeconds(20))));
        await decision;
    }

    private static Task AdvisoryAsync(Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, string key) =>
        new Npgsql.NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{key}', 0))", connection, transaction).ExecuteNonQueryAsync();

    [Fact]
    public async Task WithdrawingAnApprovalRequeuesTheBriefOfTheWeekAndTheRedraftLeavesTheRecordOut()
    {
        var (candidate, briefs) = await PublishedApprovedAsync();

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
        Assert.Equal((0, 1), await BriefSelectionAsync());
    }

    [Fact]
    public async Task ApprovingTheSameRevisionAgainAfterAWithdrawalRequeuesTheBriefAndTheRecordReturns()
    {
        var (candidate, briefs) = await PublishedApprovedAsync();
        var withdrawal = await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));
        await briefs.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3");

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 2, ReviewDecisionKind.Approve, "Fixed.", [withdrawal.DecisionId], "approve-again"));

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
        Assert.Equal((1, 0), await BriefSelectionAsync());
    }

    [Fact]
    public async Task RequestingChangesAfterAnApprovalOfThePublishedRevisionRequeuesTheBriefAndTheRecordLeavesIt()
    {
        var (candidate, briefs) = await PublishedApprovedAsync();

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.RequestChanges, "Name the bill.", [], "changes"));

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
        Assert.Equal((0, 1), await BriefSelectionAsync());
    }

    [Fact]
    public async Task AnEditAfterAWithdrawalStillLeavesThePublishedRevisionOutBecauseTheNewRevisionIsNotApproved()
    {
        var (candidate, _) = await PublishedApprovedAsync();
        await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));

        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 2, "save-after-withdrawal"));

        Assert.Equal((0, 1), await BriefSelectionAsync());
    }

    [Fact]
    public async Task ARecordWithdrawnApprovedAgainAndPublishedAtANewRevisionReturnsToTheBrief()
    {
        var (candidate, briefs) = await PublishedApprovedAsync();
        var withdrawal = await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));
        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 2, "save-after-withdrawal"));
        await Decide.ExecuteAsync(Owner, new(candidate, 3, 2, ReviewDecisionKind.Approve, "Fixed.", [withdrawal.DecisionId], "approve-new"));
        await briefs.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3");

        await new PostgresPublicationStore(factory).CommitAsync(Owner.Subject, new(2, 1, $"000002-{Guid.NewGuid():N}",
            [new(PublishedRecordKind.Statement, candidate, 3)], []), "publish", new string('a', 64), default);

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
        Assert.Equal((1, 0), await BriefSelectionAsync());
    }

    [Fact]
    public async Task ADecisionOnARevisionThatTheActiveReleaseDoesNotServeLeavesTheBriefAlone()
    {
        var (candidate, briefs) = await PublishedApprovedAsync();
        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 2, "save-newer"));

        await Decide.ExecuteAsync(Owner, new(candidate, 3, 1, ReviewDecisionKind.RequestChanges, "Name the bill.", [], "changes-newer"));

        Assert.Equal("Succeeded:3", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
    }

    [Fact]
    public async Task ADecisionOnAnActiveRevisionTakesTheBriefLockBeforeItWritesSoABriefApprovalNeverInterleavesWithIt()
    {
        var (candidate, _) = await PublishedApprovedAsync();
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");

        var withdrawing = Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");

        Assert.False(withdrawing.IsCompleted);
        await holder.DisposeAsync();
        _ = await withdrawing;
    }

    [Fact]
    public async Task ADecisionLeavesAnApprovedBriefAloneBecauseAReviewerAlreadyOwnsIt()
    {
        var (candidate, briefs) = await PublishedApprovedAsync(briefApproved: true);

        await Decide.ExecuteAsync(Owner, new(candidate, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw"));

        Assert.Equal("Succeeded:3", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
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
        var model = new FakeStatementDraftModel().Draft(Output());

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

        var settling = DraftingWorker(new FakeStatementDraftModel().Draft(Output())).ExecuteAsync(once: true);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-statement-review");
        Assert.False(settling.IsCompleted);
        await holder.DisposeAsync();
        await settling;

        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_revisions WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task ListOrdersByTierThenTheNewestEventDateAcrossPagesAndFiltersByTheStoredStatus()
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
        await SetListingAsync(requested, new DateOnly(2026, 1, 1), tier: 1);
        await SetListingAsync(approvedOlder, new DateOnly(2026, 3, 1), tier: 2);
        await SetListingAsync(approvedNewest, new DateOnly(2026, 9, 1), tier: 3);
        await SetListingAsync(awaiting, new DateOnly(2026, 6, 1), tier: 3);
        var list = new ListStatementReviews(reviews);

        async Task<PageWalk> Walk(DraftStatusFilter filter) => await PageWalk.RunAsync(async cursor =>
        {
            var page = await list.ExecuteAsync(Owner, cursor?.Encode(), 2, new DraftListFilter(filter));
            return ([.. page.Items.Select(item => item.CandidateId)], page.NewerCursor, page.OlderCursor);
        });

        (await Walk(DraftStatusFilter.All)).AssertExact([requested, approvedOlder, approvedNewest, awaiting, overCap]);
        (await Walk(DraftStatusFilter.NeedsAction)).AssertExact([requested, awaiting, overCap]);
        (await Walk(DraftStatusFilter.Approved)).AssertExact([approvedOlder, approvedNewest]);
        var everything = await list.ExecuteAsync(Owner, null, 10);
        Assert.Equal([1, 2, 3, 3, 3], everything.Items.Select(item => everything.Priorities[item.CandidateId]));
        var byId = everything.Items.ToDictionary(item => item.CandidateId);
        Assert.Null(byId[overCap].CurrentRevision);
        Assert.Equal(ReviewStatus.NotDrafted, byId[overCap].CurrentStatus);
        Assert.Equal((ReviewStatus.ChangesRequested, 1, 1), (byId[requested].CurrentStatus, byId[requested].ReviewStateVersion,
            byId[requested].UnresolvedConcernCount));
        Assert.Equal(2, byId[approvedNewest].CurrentRevision!.RevisionNumber);
        Assert.Equal("source", byId[approvedNewest].SourceId);
    }

    [Fact]
    public async Task CountsUseTheSameStatusRuleAsTheListFiltersSoADraftWithoutARevisionNeedsAction()
    {
        var overCap = await CandidateAsync();
        var awaiting = await StartedAsync();
        var approved = await ApprovableAsync();
        await InsertRevisionLessDraftAsync(overCap);
        await Decide.ExecuteAsync(Owner, new(approved, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        var list = new ListStatementReviews(reviews);

        var counts = await new GetStatementReviewCounts(reviews).ExecuteAsync(Owner);

        Assert.Equal(new StatementReviewCounts(2, 1), counts);
        Assert.Equal(counts.NeedsAction, (await list.ExecuteAsync(Owner, null, 10, new(DraftStatusFilter.NeedsAction))).Items.Length);
        Assert.Equal(counts.Approved, (await list.ExecuteAsync(Owner, null, 10, new(DraftStatusFilter.Approved))).Items.Length);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new GetStatementReviewCounts(null!).ExecuteAsync(new ReviewActor("auth0|nobody", (ReviewRole)99)));
    }

    [Fact]
    public async Task ListFiltersByIssueOfficialAndEventDateAndCombinesThemWithTheStatus()
    {
        var rent = await ApprovableAsync();
        var noIssue = await CandidateAsync(AdmittedDefinition(SoleCoverage(Markey)));
        await Create.ExecuteAsync(Owner, new(noIssue, "create-markey"));
        await SaveWithAsync(noIssue, speakers: [Markey], issues: []);
        await ExecuteAsync(connectionString, $"UPDATE statement_drafts SET event_on = '2026-02-01' WHERE candidate_id = '{rent}'");
        await ExecuteAsync(connectionString, $"UPDATE statement_drafts SET event_on = '2026-08-01' WHERE candidate_id = '{noIssue}'");
        await Decide.ExecuteAsync(Owner, new(rent, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-rent"));
        var list = new ListStatementReviews(reviews);

        async Task<string[]> Ids(DraftListFilter filter) => [.. (await list.ExecuteAsync(Owner, null, 10, filter)).Items.Select(item => item.CandidateId)];

        Assert.Equal([rent], await Ids(new(DraftStatusFilter.All, IssueId: RentIssue)));
        Assert.Equal([noIssue], await Ids(new(DraftStatusFilter.All, OfficialId: Markey)));
        Assert.Equal([noIssue], await Ids(new(DraftStatusFilter.All, From: new DateOnly(2026, 3, 1))));
        Assert.Equal([rent], await Ids(new(DraftStatusFilter.All, To: new DateOnly(2026, 3, 1))));
        Assert.Equal([rent], await Ids(new(DraftStatusFilter.Approved, IssueId: RentIssue)));
        Assert.Empty(await Ids(new(DraftStatusFilter.NeedsAction, IssueId: RentIssue)));
        Assert.Empty(await Ids(new(DraftStatusFilter.All, IssueId: RentIssue, OfficialId: Markey)));
    }

    [Fact]
    public async Task EachWritePathSetsTheListedColumnsFromTheRevisionAndTheEventDateFallsBackToTheCaptureDate()
    {
        var configuration = StatementDraftFixtures.Configuration();
        var bothSpeakers = configuration with
        {
            Sources = [configuration.Sources[0] with { Coverage = [new() { PersonId = Warren }, new() { PersonId = Markey }] }]
        };
        var candidate = await CandidateAsync(AdmittedDefinition(bothSpeakers));
        await ExecuteAsync(connectionString, $"UPDATE statement_candidates SET created_at_utc_ticks = {new DateTime(2026, 5, 4, 13, 0, 0, DateTimeKind.Utc).Ticks} WHERE candidate_id = '{candidate}'");

        await Create.ExecuteAsync(Owner, new(candidate, "create"));
        Assert.Equal(("2026-05-04", "", ""), await ListingAsync(candidate));

        await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 1, "save-a"));
        Assert.Equal(("2026-05-04", RentIssue, Warren), await ListingAsync(candidate));

        var extraction = (await drafts.ReadEvidenceAsync(candidate, default))!.Article.Extraction;
        var dated = (await SaveRequestAsync(candidate, 2, "save-b")) with
        {
            StatementDate = new DateOnly(2026, 10, 3),
            StatementDateEvidence = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf("October 3, 2026", StringComparison.Ordinal), 15),
            IssueIds = [],
            SpeakerOfficialIds = [Warren, Markey]
        };
        await Save.ExecuteAsync(Owner, dated);
        Assert.Equal(("2026-10-03", "", $"{Warren},{Markey}"), await ListingAsync(candidate));
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

    private async Task<string> CandidateAsync(CollectionJobDefinition? admittedBy = null) =>
        await seeder.SeedAsync($"{Article} Release {++candidateCount}.", $"https://example.test/pages/{candidateCount}", admittedBy);

    private async Task<string> StartedAsync()
    {
        var candidate = await CandidateAsync();
        await Create.ExecuteAsync(Owner, new(candidate, "create-" + candidate[..8]));
        return candidate;
    }

    private async Task<(string Candidate, BriefWorldSeeder Briefs)> PublishedApprovedAsync(bool briefApproved = false)
    {
        var candidate = await ApprovableAsync();
        await Decide.ExecuteAsync(Owner, new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        var briefs = new BriefWorldSeeder(connectionString);
        await briefs.ReleaseWithoutApprovalsAsync(1, BriefFixtures.InWeek, active: true, BriefWorldSeeder.Binding(PublishedRecordKind.Statement, candidate, 2));
        if (briefApproved) await briefs.BriefAsync(BriefFixtures.Week, reviewStatus: "Approved");
        else await briefs.FinishedWeekAsync(BriefFixtures.Week);
        return (candidate, briefs);
    }

    private async Task<(int Selected, int LeftOut)> BriefSelectionAsync()
    {
        var evidence = (await new PostgresBriefDraftStore(factory).ReadEvidenceAsync(BriefWeek.RecordIdOf(BriefFixtures.Week), default))!;
        return (evidence.Records.Length, evidence.LeftOut);
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
        var span = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf(Quote, StringComparison.Ordinal), Quote.Length);
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

    private AnalysisWorker<StatementDraftRun> DraftingWorker(FakeStatementDraftModel model) =>
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

    private async Task SaveWithAsync(string candidate, ImmutableArray<string> speakers, ImmutableArray<string> issues) =>
        await Save.ExecuteAsync(Owner, (await SaveRequestAsync(candidate, 1, "save-" + candidate[..8])) with
        {
            SpeakerOfficialIds = speakers,
            IssueIds = issues
        });

    private async Task SetListingAsync(string candidate, DateOnly eventOn, int tier)
    {
        await ExecuteAsync(connectionString, $"UPDATE statement_drafts SET event_on = '{eventOn:yyyy-MM-dd}' WHERE candidate_id = '{candidate}'");
        await ExecuteAsync(connectionString, $"""
            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, priority)
            VALUES ('{candidate}', '{StatementDraftTask.Task}', 'Succeeded', 0, 0, 0, 0, {tier})
            ON CONFLICT (candidate_id, task) DO UPDATE SET priority = EXCLUDED.priority
            """);
    }

    private async Task<(string EventOn, string IssueIds, string OfficialIds)> ListingAsync(string candidate)
    {
        var row = (await Relationships.ReadAsync($"""
            SELECT event_on::text, array_to_string(issue_ids, ','), array_to_string(official_ids, ',')
            FROM statement_drafts WHERE candidate_id = '{candidate}'
            """)).Single();
        return (row[0], row[1], row[2]);
    }

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
