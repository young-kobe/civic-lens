using System.Data.Common;
using CivicLens.Application.Analysis;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeReviewOutcomeStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewCatalog Catalog = new([], []);
    private readonly string schema = "outcomes_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-outcomes-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private ComparisonSeeder seeder = null!;
    private int comparisonCount;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        seeder = new ComparisonSeeder(attempts, new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
        Directory.CreateDirectory(captureRoot);
        await attempts.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task MetricsGroupAiDraftsByPromptVersionAndCountOnlyWhatReviewersDid()
    {
        var unchanged = await DraftByAiAsync();
        var edited = await DraftByAiAsync();
        var dated = await DraftByAiAsync(withChangeDate: true);
        var requested = await DraftByAiAsync();
        _ = await DraftByAiAsync();
        _ = await DraftByHandAsync();
        await DecideAsync(unchanged, 1, ReviewDecisionKind.Approve);
        await SaveAsync(edited, revision => revision with { Headline = "Edited headline" });
        await DecideAsync(edited, 2, ReviewDecisionKind.Approve);
        await SaveAsync(dated, revision => revision, changeDateChecked: true);
        await DecideAsync(dated, 2, ReviewDecisionKind.Approve);
        await DecideAsync(requested, 1, ReviewDecisionKind.RequestChanges, "Wrong deadline.");
        await ExecuteAsync(connectionString, $"""
            UPDATE document_change_analysis_runs SET prompt_version = 'aaa-old-prompt'
            WHERE draft_id IN ('{requested}', '{await LatestAiDraftAsync()}')
            """);

        var outcomes = await new GetDocumentChangeReviewOutcomes(new PostgresDocumentChangeReviewOutcomeStore(factory)).ExecuteAsync(Owner);

        Assert.Equal(["aaa-old-prompt", DocumentChangeDraftingTask.PromptVersion], outcomes.Select(item => item.PromptVersion));
        var old = outcomes[0];
        Assert.Equal((2, 0, 0, 1, 0, 1), (old.Drafts, old.ApprovedUnchanged, old.ApprovedEdited, old.ChangesRequested,
            old.ApprovalWithdrawn, old.AwaitingReview));
        var current = outcomes[1];
        Assert.Equal((3, 2, 1, 0, 0, 0), (current.Drafts, current.ApprovedUnchanged, current.ApprovedEdited, current.ChangesRequested,
            current.ApprovalWithdrawn, current.AwaitingReview));
        Assert.Equal(1, current.FieldEdits[DocumentChangeField.Headline]);
        Assert.Equal(0, current.FieldEdits[DocumentChangeField.ChangeDate]);
    }

    [Fact]
    public async Task AWithdrawnApprovalAndACarriedConcernUseTheCoreStatusRules()
    {
        var withdrawn = await DraftByAiAsync();
        var carried = await DraftByAiAsync();
        await DecideAsync(withdrawn, 1, ReviewDecisionKind.Approve);
        await DecideAsync(withdrawn, 1, ReviewDecisionKind.WithdrawApproval, "Source misread.", stateVersion: 1);
        await DecideAsync(carried, 1, ReviewDecisionKind.RequestChanges, "Fix the summary.");
        await SaveAsync(carried, revision => revision with { Summary = "Fixed summary." });

        var outcome = Assert.Single(await new GetDocumentChangeReviewOutcomes(new PostgresDocumentChangeReviewOutcomeStore(factory)).ExecuteAsync(Owner));

        Assert.Equal((2, 1, 1), (outcome.Drafts, outcome.ApprovalWithdrawn, outcome.AwaitingReview));
    }

    [Fact]
    public async Task APageCostsTheSameQueriesForOneDraftAndForAFullPageSoTheReadIsBatched()
    {
        for (var index = 0; index < 4; index++) _ = await DraftByAiAsync();
        var counter = new ReadCommandCounter();
        var counted = new PostgresDocumentChangeReviewOutcomeStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));

        await counted.ListAiDraftsAsync(null, 1, default);
        var singleDraftReads = counter.ReadCount;
        counter.ReadCount = 0;
        var page = await counted.ListAiDraftsAsync(null, 4, default);

        Assert.Equal(4, page.Items.Length);
        Assert.Equal(singleDraftReads, counter.ReadCount);
        Assert.InRange(singleDraftReads, 1, 4);
    }

    [Fact]
    public async Task KeysetPagesVisitEveryAiDraftOnceAndSkipHandDrafts()
    {
        var expected = new List<string>();
        for (var index = 0; index < 3; index++) expected.Add(await DraftByAiAsync());
        _ = await DraftByHandAsync();
        var store = new PostgresDocumentChangeReviewOutcomeStore(factory);
        var seen = new List<string>();
        PageCursor? cursor = null;

        do
        {
            var slice = await store.ListAiDraftsAsync(cursor, 2, default);
            seen.AddRange(slice.Items.Select(item => item.Current.DraftId));
            cursor = PageCursor.Parse(slice.OlderCursor);
        }
        while (cursor is not null);

        Assert.Equal(expected.Order(), seen.Order());
    }

    private async Task<string> DraftByAiAsync(bool withChangeDate = false)
    {
        var number = ++comparisonCount;
        var after = withChangeDate ? $"Updated October 3, 2026. Deadline: October {10 + number}.\n" : $"Deadline: October {10 + number}.\n";
        var comparison = await seeder.SaveAsync("Deadline: October 1.\n", after);
        var changeDate = withChangeDate ? new { date = "2026-10-03", citationIndex = 0, confidence = 0.9 } : null;
        var output = FakeDocumentChangeDraftingModel.Output("Headline",
            [("h1", "after", withChangeDate ? "Updated October 3, 2026." : $"October {10 + number}")], changeDate: changeDate);
        var model = new FakeDocumentChangeDraftingModel().Draft(output);
        var worker = new DocumentChangeAnalysisWorker(new PostgresDocumentChangeAnalysisStore(factory),
            new PostgresDocumentChangeAnalysisStatusStore(factory), model, new DocumentChangeAnalysisSettings(10_000_000, concurrency: 1),
            new DocumentChangeAnalysisCatalog([], []), TimeProvider.System);
        await worker.ExecuteAsync(once: true);
        var entry = Assert.Single(await new PostgresDocumentChangeAnalysisStore(factory).GetByComparisonIdsAsync([comparison.ComparisonId], default));
        return entry.DraftId!;
    }

    private async Task<string> DraftByHandAsync()
    {
        var comparison = await seeder.SaveAsync("Hand one.\n", $"Hand two {++comparisonCount}.\n");
        var draft = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "hand-" + comparisonCount));
        await SaveAsync(draft.DraftId, revision => revision with { Headline = "Hand headline", Summary = "Hand summary.", Institution = "Agency" });
        await DecideAsync(draft.DraftId, 2, ReviewDecisionKind.Approve);
        return draft.DraftId;
    }

    private async Task<string> LatestAiDraftAsync() => (string)(await ScalarAsync(
        "SELECT draft_id FROM document_change_drafts d WHERE EXISTS (SELECT 1 FROM document_change_analysis_runs r WHERE r.draft_id = d.draft_id) " +
        "ORDER BY created_at_utc_ticks DESC, draft_id DESC LIMIT 1"))!;

    private Task DecideAsync(string draftId, int revision, ReviewDecisionKind kind, string? note = null, int stateVersion = 0) =>
        new DecideDocumentChangeReview(reviews, Catalog).ExecuteAsync(Owner,
            new DecideDocumentChangeReviewRequest(draftId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N")));

    private async Task SaveAsync(string draftId, Func<DocumentChangeDraftRevision, DocumentChangeDraftRevision> edit,
        bool changeDateChecked = false)
    {
        var review = (await reviews.GetAsync(draftId, default))!;
        var current = review.CurrentRevision;
        var next = edit(current);
        await new SaveDocumentChangeDraft(reviews, Catalog).ExecuteAsync(Owner, new SaveDocumentChangeDraftRequest(draftId,
            current.RevisionNumber, next.Headline, next.Summary, next.Significance, next.Limits, next.Institution, next.ChangeDate,
            next.ChangeDateEvidence, next.OfficialIds, next.IssueIds, next.Citations, Guid.NewGuid().ToString("N"), changeDateChecked));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
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
