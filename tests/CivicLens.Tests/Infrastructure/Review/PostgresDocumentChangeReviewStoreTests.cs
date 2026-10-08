using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Review;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Tests.Host;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "review_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-review-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA {schema}";
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        comparisons = new PostgresDocumentComparisonStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        await attempts.MigrateAsync();
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
        await command.ExecuteNonQueryAsync();
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task CreateReplayIsStableAndConcurrentSavesAppendOnlyOneCurrentRevision()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var create = new CreateDocumentChangeDraft(reviews);
        var createRequest = new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "create-key");
        var first = await create.ExecuteAsync(actor, createRequest);
        var replay = await create.ExecuteAsync(actor, createRequest);
        Assert.Equal(first.DraftId, replay.DraftId);
        Assert.NotEmpty(replay.Citations);

        var save = new SaveDocumentChangeDraft(reviews, catalog);
        var initialRequest = new SaveDocumentChangeDraftRequest(first.DraftId, 1, "Policy update", "The policy changed.",
            null, "The record does not establish why the source changed.", "Example Office", null, null,
            [], [], first.Citations, "save-a");
        var firstSave = save.ExecuteAsync(actor, initialRequest);
        var competingSave = save.ExecuteAsync(actor, initialRequest with { Headline = "Competing edit", IdempotencyKey = "save-b" });
        var outcomes = await Task.WhenAll(ObserveAsync(firstSave), ObserveAsync(competingSave));
        Assert.Single(outcomes, outcome => outcome is DocumentChangeDraftRevision);
        Assert.Single(outcomes, outcome => outcome is DocumentChangeReviewConflictException);

        var review = await reviews.GetAsync(first.DraftId, CancellationToken.None);
        Assert.NotNull(review);
        Assert.Equal(2, review.Revisions.Length);
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
    }

    [Fact]
    public async Task DecisionReplayReturnsOriginalDecisionAndRejectsPayloadReuse()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var catalog = new ReviewCatalog([], []);
        var revision = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(actor,
            new(comparison.ComparisonId, "create-key"));
        revision = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor,
            new(revision.DraftId, 1, "Policy update", "The policy changed.", null, null, "Example Office",
                null, null, [], [], revision.Citations, "save-key"));
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        var request = new DecideDocumentChangeReviewRequest(revision.DraftId, 2, 0,
            ReviewDecisionKind.RequestChanges, "Please explain the before and after context.", [], "decision-key");
        var first = await decide.ExecuteAsync(actor, request);
        var replay = await decide.ExecuteAsync(actor, request);
        Assert.Equal(first.DecisionId, replay.DecisionId);
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            request with { Note = "Different payload." }));
        var detail = await reviews.GetAsync(revision.DraftId, CancellationToken.None);
        Assert.Equal(first.DecisionId, Assert.Single(detail!.UnresolvedConcerns).DecisionId);
    }

    [Fact]
    public async Task ComparisonPagesTraverseIneligibleRowsWithoutOmissionsOrDuplicates()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 12; index++)
        {
            var before = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Before."), "parser", "normalizer", "Before.");
            var changed = index % 3 == 0;
            var text = changed ? "After." : "Before.";
            var after = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync(text), "parser", "normalizer", text);
            await extractions.SaveAsync(before, default);
            await extractions.SaveAsync(after, default);
            var comparison = await comparisons.SaveAsync(CivicLens.Core.Documents.DocumentComparison.Create(before, after), default);
            if (changed) expected.Add(comparison.ComparisonId);
        }
        var actual = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await reviews.ListEligibleComparisonsAsync(cursor, 1, default);
            actual.AddRange(page.Items.Select(item => item.Comparison.ComparisonId));
            Assert.True(page.NextCursor is null || string.CompareOrdinal(page.NextCursor, cursor) > 0);
            cursor = page.NextCursor;
            Assert.True(++pages <= 12);
        } while (cursor is not null);
        Assert.Equal(expected.Order(), actual.Order());
        Assert.Equal(actual.Count, actual.Distinct().Count());
    }

    [Fact]
    public async Task EvidenceRangesAndApprovalRequirementsAreEnforced()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var revision = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(actor, new(comparison.ComparisonId, "create"));
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            new(revision.DraftId, 1, 0, ReviewDecisionKind.Approve, null, [], "incomplete")));
        var request = new SaveDocumentChangeDraftRequest(revision.DraftId, 1, "Headline", "Summary", null, null,
            "Institution", null, null, [], [], [new(comparison.BeforeExtractionId, int.MaxValue, 1)], "bad-range");
        await Assert.ThrowsAsync<ArgumentException>(() => new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor, request));
        request = request with { Citations = revision.Citations, IdempotencyKey = "valid" };
        var saved = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor, request);
        await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        var concern = await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 1, ReviewDecisionKind.WithdrawApproval, "Context missing", [], "withdraw"));
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            new(saved.DraftId, 2, 2, ReviewDecisionKind.Approve, null, [], "unresolved")));
        await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 2, ReviewDecisionKind.Approve, "Context checked", [concern.DecisionId], "resolved"));
        var detail = await reviews.GetAsync(saved.DraftId, default);
        Assert.Empty(detail!.UnresolvedConcerns);
        Assert.Equal(actor.Subject, detail.CurrentRevision.AuthorSubject);
    }

    private async Task<CivicLens.Core.Documents.DocumentComparison> SaveComparisonAsync()
    {
        var before = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Policy before."), "parser", "normalizer", "Policy before.");
        var after = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Policy after."), "parser", "normalizer", "Policy after.");
        await extractions.SaveAsync(before, CancellationToken.None);
        await extractions.SaveAsync(after, CancellationToken.None);
        return await comparisons.SaveAsync(CivicLens.Core.Documents.DocumentComparison.Create(before, after), CancellationToken.None);
    }

    private async Task<CivicLens.Core.Collection.CapturedAttemptResult> ImportAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(captureRoot, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var request = Request() with { JobId = Guid.NewGuid().ToString("N"), ArtifactDirectory = captureRoot };
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var result = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            request.JobId, request, receipt), CancellationToken.None);
        return (CivicLens.Core.Collection.CapturedAttemptResult)result.AttemptResult;
    }

    private static async Task<object> ObserveAsync(Task<DocumentChangeDraftRevision> task)
    {
        try { return await task; }
        catch (DocumentChangeReviewConflictException exception) { return exception; }
    }

}
