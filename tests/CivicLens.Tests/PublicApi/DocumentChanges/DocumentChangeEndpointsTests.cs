using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.PublicApi.DocumentChanges;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DocumentChangeEndpointsTests(PostgresCollection postgres)
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task InvalidAndUnknownDocumentChangesAnswerNotFound()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        using var invalid = await host.Client.GetAsync("/v1/document-changes/bad-id");
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);

        var seeded = await CreateDraftAsync(host, "unknown-target", "A changed source.", "A newer source.");
        var published = await SaveAsync(host, seeded, "Published", seeded.Citations);
        await ApproveAsync(host, published);
        await CommitAsync(host, 1, null, published);
        using var wrongKind = await host.Client.GetAsync("/v1/document-changes/" + new string('f', 64));
        Assert.Equal(HttpStatusCode.NotFound, wrongKind.StatusCode);
        using var unknown = await host.Client.GetAsync("/v1/document-changes/" + new string('b', 32));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task MissingActiveReleaseAnswersServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        using var response = await host.Client.GetAsync("/v1/document-changes/" + new string('a', 32));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AnActiveEmptyReleaseAnswersNotFoundRatherThanServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, DateTimeOffset.UnixEpoch, recordCount: 0);

        using var response = await host.Client.GetAsync("/v1/document-changes/" + new string('a', 32));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DetailReturnsCompletePinnedSourcesOrderedChangesAndExactCitationQuotes()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        var created = await CreateDraftAsync(host, "detail", "A blue \U00010400 fee.\n\nStable line.\n\nA blue tax.",
            "A green \U00010401 fee.\n\nStable line.\n\nA red tax.");
        var saved = await SaveAsync(host, created, "Headline", created.Citations);
        await ApproveAsync(host, saved);
        await CommitAsync(host, 1, null, saved);

        using var response = await host.Client.GetAsync("/v1/document-changes/" + saved.DraftId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(1, root.GetProperty("release").GetInt32());
        Assert.Equal(saved.RevisionNumber, root.GetProperty("documentChange").GetProperty("revisionNumber").GetInt32());
        AssertSource(root.GetProperty("before"), created.BeforeUrl, created.BeforeText, created.BeforeObservedAt);
        AssertSource(root.GetProperty("after"), created.AfterUrl, created.AfterText, created.AfterObservedAt);
        AssertChanges(root.GetProperty("changes"), created.Comparison);
        AssertCitations(root, saved.Citations);
    }

    [Fact]
    public async Task DateEvidenceRemainsInPublicCitationsWhenItIsTheOnlyCitationAndWithAnotherCitation()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        var created = await CreateDraftAsync(host, "date-only", "Published on June 4, 2026. Old wording.",
            "Published on June 4, 2026. New wording.");
        var dateSpan = new SourceSpan(created.AfterExtractionId, created.AfterText.IndexOf("June 4, 2026", StringComparison.Ordinal), 12);
        var dateOnly = await SaveAsync(host, created, "Date only", [dateSpan], new DateOnly(2026, 6, 4), dateSpan);
        await ApproveAsync(host, dateOnly);
        await CommitAsync(host, 1, null, dateOnly);
        await AssertDateEvidenceAsync(host, dateOnly, 1);

        var regularCitation = new SourceSpan(created.BeforeExtractionId, created.BeforeText.IndexOf("Old wording", StringComparison.Ordinal), "Old wording".Length);
        var withOrdinaryCitation = await SaveAsync(host, dateOnly, "Date with citation", [dateSpan, regularCitation],
            new DateOnly(2026, 6, 4), dateSpan);
        await ApproveAsync(host, withOrdinaryCitation);
        await CommitAsync(host, 2, 1, withOrdinaryCitation);
        await AssertDateEvidenceAsync(host, withOrdinaryCitation, 2);
    }

    [Fact]
    public async Task DetailStaysOnActiveApprovedRevisionUntilActivationAndReturnsExactRevisionAfterRollback()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        var created = await CreateDraftAsync(host, "pinned", "Before wording.", "After wording.");
        var revisionOne = await SaveAsync(host, created, "First published headline", created.Citations);
        await ApproveAsync(host, revisionOne);
        await CommitAsync(host, 1, null, revisionOne);
        var first = await GetDetailAsync(host, revisionOne.DraftId);
        Assert.Equal("person", first.GetProperty("documentChange").GetProperty("textOrigin").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("documentChange").GetProperty("label").ValueKind);

        var revisionTwo = await SaveAsync(host, revisionOne, "Unpublished headline", revisionOne.Citations);
        await SetModelAuthorAsync(host.OperationalDatabase, revisionTwo.DraftId, revisionTwo.RevisionNumber);
        var whileUnapproved = await GetDetailAsync(host, revisionOne.DraftId);
        Assert.Equal(first.GetRawText(), whileUnapproved.GetRawText());
        await ApproveAsync(host, revisionTwo);
        await CommitAsync(host, 2, 1, revisionTwo);
        var second = await GetDetailAsync(host, revisionOne.DraftId);
        Assert.Equal(2, second.GetProperty("release").GetInt32());
        Assert.Equal(revisionTwo.RevisionNumber, second.GetProperty("documentChange").GetProperty("revisionNumber").GetInt32());
        Assert.Equal("Unpublished headline", second.GetProperty("documentChange").GetProperty("headline").GetString());
        Assert.Equal("model", second.GetProperty("documentChange").GetProperty("textOrigin").GetString());
        Assert.Equal(PublishedWording.ModelDocumentChange, second.GetProperty("documentChange").GetProperty("label").GetString());

        await ActivateReleaseAsync(host.OperationalDatabase, 1);
        var rolledBack = await GetDetailAsync(host, revisionOne.DraftId);
        Assert.Equal(first.GetRawText(), rolledBack.GetRawText());
    }

    [Theory]
    [InlineData("missing-count")]
    [InlineData("oversized-count")]
    [InlineData("zero-with-children")]
    [InlineData("missing-hunks")]
    [InlineData("extra-hunk")]
    [InlineData("wrong-word-count")]
    [InlineData("oversized-word-count")]
    [InlineData("missing-edits")]
    [InlineData("edit-position-gap")]
    [InlineData("split-citation")]
    [InlineData("split-change")]
    [InlineData("unexpected-date-evidence")]
    [InlineData("oversized-issues")]
    [InlineData("code-author")]
    public async Task DetailFailsClosedWhenPublishedEvidenceIsIncompleteOrInvalid(string corruption)
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        var created = await CreateDraftAsync(host, "corrupt-count", "Old \U00010400 rule.", "New \U00010401 rule.");
        var draft = await SaveAsync(host, created, "Changed rule", created.Citations);
        await ApproveAsync(host, draft);
        await CommitAsync(host, 1, null, draft);

        var sql = corruption switch
        {
            "missing-count" => "UPDATE document_comparisons SET hunk_count = NULL WHERE comparison_id = @id",
            "oversized-count" => "UPDATE document_comparisons SET hunk_count = 2001 WHERE comparison_id = @id",
            "zero-with-children" => "UPDATE document_comparisons SET hunk_count = 0, has_changes = false WHERE comparison_id = @id",
            "missing-hunks" => "DELETE FROM document_comparison_hunks WHERE comparison_id = @id",
            "extra-hunk" => """
                INSERT INTO document_comparison_hunks
                    (comparison_id, position, before_start, before_length, after_start, after_length,
                     before_context_start, before_context_length, after_context_start, after_context_length, word_edit_count)
                SELECT comparison_id, 2000, before_start, before_length, after_start, after_length,
                       before_context_start, before_context_length, after_context_start, after_context_length, 0
                  FROM document_comparison_hunks WHERE comparison_id = @id AND position = 0
                """,
            "wrong-word-count" => "UPDATE document_comparison_hunks SET word_edit_count = word_edit_count + 1 WHERE comparison_id = @id",
            "oversized-word-count" => "UPDATE document_comparison_hunks SET word_edit_count = 513 WHERE comparison_id = @id",
            "missing-edits" => "DELETE FROM document_comparison_word_edits WHERE comparison_id = @id",
            "edit-position-gap" => "UPDATE document_comparison_word_edits SET position = position + 1000 WHERE comparison_id = @id",
            "split-citation" => """
                UPDATE document_change_revision_citations SET citation_start = 5, citation_length = 1
                 WHERE draft_id = @draft AND revision_number = @revision
                """,
            "split-change" => "UPDATE document_comparison_hunks SET before_start = 5, before_length = 1 WHERE comparison_id = @id",
            "unexpected-date-evidence" => """
                UPDATE document_change_revision_citations SET is_change_date_evidence = true
                 WHERE draft_id = @draft AND revision_number = @revision AND position = 0
                """,
            "oversized-issues" => """
                INSERT INTO document_change_revision_issues (draft_id, revision_number, issue_id, position)
                SELECT @draft, @revision, 'issue-' || n, 0 FROM generate_series(0, 2 * @maximum_issues) n
                """,
            "code-author" => "UPDATE document_change_revisions SET author_kind = 'Code' WHERE draft_id = @draft AND revision_number = @revision",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        await using (var connection = new NpgsqlConnection(host.OperationalDatabase))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", draft.ComparisonId);
            command.Parameters.AddWithValue("draft", draft.DraftId);
            command.Parameters.AddWithValue("revision", draft.RevisionNumber);
            command.Parameters.AddWithValue("maximum_issues", PublishedRecordEntry.MaximumFilterValues);
            Assert.True(await command.ExecuteNonQueryAsync() > 0);
            if (corruption == "oversized-issues")
            {
                await using var readAsPublic = new NpgsqlCommand("SET ROLE civic_lens_public", connection);
                await readAsPublic.ExecuteNonQueryAsync();
                await using var boundedIssues = new NpgsqlCommand(
                    "SELECT cardinality(issue_ids) FROM public_api.document_changes WHERE record_id = @draft", connection);
                boundedIssues.Parameters.AddWithValue("draft", draft.DraftId);
                Assert.Equal(PublishedRecordEntry.MaximumFilterValues + 1, (int)(await boundedIssues.ExecuteScalarAsync())!);
            }
        }

        using var response = await host.Client.GetAsync("/v1/document-changes/" + draft.DraftId);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static async Task<int> ReadStateVersionAsync(string database, string recordId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT review_state_version FROM document_change_drafts WHERE draft_id = @id", connection);
        command.Parameters.AddWithValue("id", recordId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<CreatedDraft> CreateDraftAsync(ApiHost host, string key, string beforeText, string afterText)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "document-change-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(captureRoot);
        try
        {
            var factory = Factory(host.OperationalDatabase);
            var comparison = await new ComparisonSeeder(new PostgresCollectionAttemptStore(factory),
                new PostgresDocumentExtractionStore(factory), new PostgresDocumentComparisonStore(factory), captureRoot)
                .SaveAsync(beforeText, afterText, "https://example.test/pages/before", "https://example.test/pages/after");
            var draft = await new CreateDocumentChangeDraft(new PostgresDocumentChangeReviewStore(factory)).ExecuteAsync(Owner,
                new(comparison.ComparisonId, "create-" + key));
            var before = await ReadSourceAsync(host.OperationalDatabase, comparison.BeforeExtractionId);
            var after = await ReadSourceAsync(host.OperationalDatabase, comparison.AfterExtractionId);
            return new(draft, comparison, beforeText, afterText, before.Url, after.Url, before.ObservedAt, after.ObservedAt);
        }
        finally
        {
            Directory.Delete(captureRoot, recursive: true);
        }
    }

    private static async Task<DocumentChangeDraftRevision> SaveAsync(ApiHost host, CreatedDraft created, string headline,
        System.Collections.Immutable.ImmutableArray<SourceSpan> citations, DateOnly? changeDate = null, SourceSpan? dateEvidence = null) =>
        await SaveAsync(host, created.Draft, headline, citations, changeDate, dateEvidence);

    private static async Task<DocumentChangeDraftRevision> SaveAsync(ApiHost host, DocumentChangeDraftRevision draft, string headline,
        System.Collections.Immutable.ImmutableArray<SourceSpan> citations, DateOnly? changeDate = null, SourceSpan? dateEvidence = null)
    {
        var store = new PostgresDocumentChangeReviewStore(Factory(host.OperationalDatabase));
        return await new SaveDocumentChangeDraft(store, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, headline, "The source text changed.", null, null, "Example Office",
                changeDate, dateEvidence, [], [], citations, "save-" + Guid.NewGuid().ToString("N"), ChangeDateChecked: changeDate is not null));
    }

    private static async Task ApproveAsync(ApiHost host, DocumentChangeDraftRevision draft)
    {
        var store = new PostgresDocumentChangeReviewStore(Factory(host.OperationalDatabase));
        var stateVersion = await ReadStateVersionAsync(host.OperationalDatabase, draft.DraftId);
        await new DecideDocumentChangeReview(store, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, stateVersion, ReviewDecisionKind.Approve, null, [],
                "approve-" + Guid.NewGuid().ToString("N")));
    }

    private static async Task CommitAsync(ApiHost host, int release, int? baseRelease, DocumentChangeDraftRevision draft)
    {
        var publications = new PostgresPublicationStore(Factory(host.OperationalDatabase), TestCodeVersion.Current);
        var stateVersion = await ReadStateVersionAsync(host.OperationalDatabase, draft.DraftId);
        await publications.CommitAsync(Owner.Subject,
            new PublicationCommit(release, baseRelease, $"{release:D6}-{Guid.NewGuid():N}",
                [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber)],
                [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber, stateVersion)])
            { Withdrawals = [], Names = ReleaseNameFixtures.None }, "release-" + release, Hash("release-" + release), default);
        await publications.ActivateAsync(release, (_, _) => Task.CompletedTask, default);
    }

    private static Task ActivateReleaseAsync(string database, int release) =>
        new PostgresPublicationStore(Factory(database), TestCodeVersion.Current).ActivateAsync(release, (_, _) => Task.CompletedTask, default);

    private static async Task SetModelAuthorAsync(string database, string draftId, int revisionNumber)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE document_change_revisions SET author_kind = 'Analysis', author_subject = @author WHERE draft_id = @draft AND revision_number = @revision", connection);
        command.Parameters.AddWithValue("author", ReviewAuthor.AnalysisSubject);
        command.Parameters.AddWithValue("draft", draftId);
        command.Parameters.AddWithValue("revision", revisionNumber);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<System.Text.Json.JsonElement> GetDetailAsync(ApiHost host, string recordId)
    {
        using var response = await host.Client.GetAsync("/v1/document-changes/" + recordId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static void AssertSource(System.Text.Json.JsonElement source, string url, string text, DateTimeOffset observedAt)
    {
        Assert.Equal(url, source.GetProperty("url").GetString());
        Assert.Equal(text, source.GetProperty("text").GetString());
        Assert.Equal(observedAt, source.GetProperty("observedAtUtc").GetDateTimeOffset());
    }

    private static void AssertChanges(System.Text.Json.JsonElement actual, CivicLens.Core.Documents.DocumentComparison comparison)
    {
        var changes = actual.EnumerateArray().ToArray();
        Assert.Equal(comparison.Hunks.Length, changes.Length);
        for (var position = 0; position < changes.Length; position++)
        {
            var expected = comparison.Hunks[position];
            var change = changes[position];
            Assert.Equal(expected.BeforeStart, change.GetProperty("beforeStart").GetInt32());
            Assert.Equal(expected.BeforeLength, change.GetProperty("beforeLength").GetInt32());
            Assert.Equal(expected.AfterStart, change.GetProperty("afterStart").GetInt32());
            Assert.Equal(expected.AfterLength, change.GetProperty("afterLength").GetInt32());
            Assert.Equal(expected.BeforeContextStart, change.GetProperty("beforeContextStart").GetInt32());
            Assert.Equal(expected.BeforeContext.Length, change.GetProperty("beforeContextLength").GetInt32());
            Assert.Equal(expected.AfterContextStart, change.GetProperty("afterContextStart").GetInt32());
            Assert.Equal(expected.AfterContext.Length, change.GetProperty("afterContextLength").GetInt32());
            var edits = change.GetProperty("wordEdits").EnumerateArray().ToArray();
            Assert.Equal(expected.WordEdits.Length, edits.Length);
            for (var index = 0; index < edits.Length; index++)
            {
                Assert.Equal(expected.WordEdits[index].BeforeStart, edits[index].GetProperty("beforeStart").GetInt32());
                Assert.Equal(expected.WordEdits[index].BeforeLength, edits[index].GetProperty("beforeLength").GetInt32());
                Assert.Equal(expected.WordEdits[index].AfterStart, edits[index].GetProperty("afterStart").GetInt32());
                Assert.Equal(expected.WordEdits[index].AfterLength, edits[index].GetProperty("afterLength").GetInt32());
            }
        }
    }

    private static void AssertCitations(System.Text.Json.JsonElement root, System.Collections.Immutable.ImmutableArray<SourceSpan> expected)
    {
        var citations = root.GetProperty("citations").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, citations.Length);
        foreach (var (span, index) in expected.Select((span, index) => (span, index)))
        {
            var citation = citations[index];
            var source = span.ExtractionId == root.GetProperty("before").GetProperty("extractionId").GetString()
                ? root.GetProperty("before") : root.GetProperty("after");
            Assert.Equal(span.ExtractionId, citation.GetProperty("extractionId").GetString());
            Assert.Equal(span.Start, citation.GetProperty("start").GetInt32());
            Assert.Equal(span.Length, citation.GetProperty("length").GetInt32());
            Assert.Equal(source.GetProperty("text").GetString()!.Substring(span.Start, span.Length), citation.GetProperty("quote").GetString());
        }
    }

    private static async Task AssertDateEvidenceAsync(ApiHost host, DocumentChangeDraftRevision draft, int release)
    {
        var root = await GetDetailAsync(host, draft.DraftId);
        Assert.Equal(release, root.GetProperty("release").GetInt32());
        Assert.Equal(draft.ChangeDate!.Value.ToString("yyyy-MM-dd"), root.GetProperty("documentChange").GetProperty("changeDate").GetString());
        var dateEvidence = root.GetProperty("changeDateEvidence");
        Assert.Equal(draft.ChangeDateEvidence!.Start, dateEvidence.GetProperty("start").GetInt32());
        Assert.Equal("June 4, 2026", dateEvidence.GetProperty("quote").GetString());
        AssertCitations(root, draft.Citations);
    }

    private static PooledDbContextFactory<CollectionAttemptDbContext> Factory(string database) =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);

    private static async Task<(string Url, DateTimeOffset ObservedAt)> ReadSourceAsync(string database, string extractionId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT a.final_url, a.observed_at_utc_ticks
              FROM collection_attempts a JOIN document_extractions e ON e.attempt_id = a.attempt_id
             WHERE e.extraction_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", extractionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero));
    }

    private sealed record CreatedDraft(DocumentChangeDraftRevision Draft, CivicLens.Core.Documents.DocumentComparison Comparison,
        string BeforeText, string AfterText, string BeforeUrl, string AfterUrl, DateTimeOffset BeforeObservedAt,
        DateTimeOffset AfterObservedAt)
    {
        public System.Collections.Immutable.ImmutableArray<SourceSpan> Citations => Draft.Citations;
        public string BeforeExtractionId => Comparison.BeforeExtractionId;
        public string AfterExtractionId => Comparison.AfterExtractionId;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
