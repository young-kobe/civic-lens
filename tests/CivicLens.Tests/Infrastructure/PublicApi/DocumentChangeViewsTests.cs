using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DocumentChangeViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "document-change-views-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private PostgresPublicationStore publications = null!;
    private ComparisonSeeder comparisons = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        publications = new PostgresPublicationStore(factory, TestCodeVersion.Current);
        comparisons = new ComparisonSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ViewsFollowThePublishedRevisionAndReleaseAndPreserveOrderedEvidenceAndFinalUrls()
    {
        var published = await CreateDraftAsync("published");
        var revisionTwo = await SaveRevisionAsync(published, "Published wording");
        await ApproveAsync(revisionTwo);
        await CommitAsync(1, null, revisionTwo);

        var unpublished = await CreateDraftAsync("unpublished");
        var currentRevision = await SaveRevisionAsync(revisionTwo, "Mutable current wording");

        var firstRelease = await ReadPublishedParentAsync(published.DraftId);
        Assert.NotNull(firstRelease);
        Assert.Equal((published.DraftId, 2, 1, "Published wording"),
            (firstRelease.Value.RecordId, firstRelease.Value.Revision, firstRelease.Value.Release, firstRelease.Value.Headline));
        Assert.True(firstRelease.Value.HunkCount is > 0);
        Assert.Null(await ReadPublishedParentAsync(unpublished.DraftId));
        var sources = await ReadSourcesAsync(published.DraftId);
        Assert.Collection(sources,
            source => Assert.Equal(("Before", "https://example.test/pages/before", "A blue fee.\n\nStable line.\n\nA blue tax."), source),
            source => Assert.Equal(("After", "https://example.test/pages/after", "A green fee.\n\nStable line.\n\nA red tax."), source));

        var hunks = await ReadHunksAsync(published.DraftId);
        Assert.NotEmpty(hunks);
        Assert.Equal(firstRelease.Value.HunkCount, hunks.Count);
        Assert.Equal(Enumerable.Range(0, hunks.Count), hunks.Select(hunk => hunk.Position));

        var edits = await ReadWordEditsAsync(published.DraftId);
        Assert.NotEmpty(edits);
        Assert.Equal(edits.OrderBy(edit => edit.HunkPosition).ThenBy(edit => edit.Position), edits);
        foreach (var group in edits.GroupBy(edit => edit.HunkPosition))
            Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(edit => edit.Position));
        Assert.Equal(hunks.Select(hunk => (hunk.Position, hunk.WordEditCount)),
            hunks.Select(hunk => (hunk.Position, edits.Count(edit => edit.HunkPosition == hunk.Position))));

        var citations = await ReadCitationsAsync(published.DraftId);
        Assert.Equal(revisionTwo.Citations.Select((citation, position) =>
                (position, citation.ExtractionId, citation.Start, citation.Length)), citations);

        await ApproveAsync(currentRevision);
        await CommitAsync(2, 1, currentRevision);
        Assert.Equal((published.DraftId, 3, 2, "Mutable current wording", firstRelease.Value.HunkCount),
            await ReadPublishedParentAsync(published.DraftId));
        Assert.Null(await ReadPublishedParentAsync(unpublished.DraftId));

        await ActivateReleaseAsync(1);
        Assert.Equal(firstRelease, await ReadPublishedParentAsync(published.DraftId));

        await SetHunkCountNullAsync(revisionTwo.ComparisonId);
        var legacyMarker = await ReadPublishedParentAsync(published.DraftId);
        Assert.Equal((published.DraftId, 2, 1, "Published wording", (int?)null), legacyMarker);
    }

    private async Task<DocumentChangeDraftRevision> CreateDraftAsync(string key)
    {
        var before = key == "published"
            ? "A blue fee.\n\nStable line.\n\nA blue tax."
            : "A new rule.\n\nUnchanged section.\n\nOld limit.";
        var after = key == "published"
            ? "A green fee.\n\nStable line.\n\nA red tax."
            : "A new rule.\n\nUnchanged section.\n\nNew limit.";
        var comparison = await comparisons.SaveAsync(before, after,
            "https://example.test/pages/before", "https://example.test/pages/after");
        return await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner,
            new(comparison.ComparisonId, "create-" + key));
    }

    private Task<DocumentChangeDraftRevision> SaveRevisionAsync(DocumentChangeDraftRevision draft, string headline) =>
        new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, headline, "The text changed.", null, null, "Example Office",
                null, null, [], [], draft.Citations, "save-" + headline.Replace(' ', '-')));

    private async Task ApproveAsync(DocumentChangeDraftRevision draft)
    {
        var stateVersion = await ReadStateVersionAsync(draft.DraftId);
        await new DecideDocumentChangeReview(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, stateVersion, ReviewDecisionKind.Approve, null, [], "approve-" + draft.RevisionNumber));
    }

    private async Task CommitAsync(int release, int? baseRelease, DocumentChangeDraftRevision draft)
    {
        var stateVersion = await ReadStateVersionAsync(draft.DraftId);
        var record = new PublishedRevisionBinding(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber);
        await publications.CommitAsync(Owner.Subject,
            new PublicationCommit(release, baseRelease, $"{release:D6}-{Guid.NewGuid():N}", [record],
                [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber, stateVersion)])
            { Withdrawals = [], Names = ReleaseNameFixtures.None },
            "release-" + release, Hash(release), default);
        await publications.ActivateAsync(release, (_, _) => Task.CompletedTask, default);
    }

    private async Task<(string RecordId, int Revision, int Release, string Headline, int? HunkCount)?> ReadPublishedParentAsync(string recordId)
    {
        await using var connection = await OpenViewsAsync();
        await using var command = new NpgsqlCommand("""
            SELECT record_id, revision_number, release_number, headline, hunk_count
              FROM public_api.document_changes WHERE record_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4));
    }

    private async Task<List<(string Side, string Url, string Text)>> ReadSourcesAsync(string recordId)
    {
        await using var connection = await OpenViewsAsync();
        await using var command = new NpgsqlCommand("""
            SELECT source_side, url, text FROM public_api.document_change_sources
             WHERE record_id = @id AND revision_number = 2
             ORDER BY CASE source_side WHEN 'Before' THEN 0 ELSE 1 END
            """, connection);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    private async Task<List<(int Position, int WordEditCount)>> ReadHunksAsync(string recordId)
    {
        await using var connection = await OpenViewsAsync();
        await using var command = new NpgsqlCommand("""
            SELECT position, word_edit_count FROM public_api.document_change_hunks
             WHERE record_id = @id AND revision_number = 2 ORDER BY position
            """, connection);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(int, int)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return rows;
    }

    private async Task<List<(int HunkPosition, int Position)>> ReadWordEditsAsync(string recordId)
    {
        await using var connection = await OpenViewsAsync();
        await using var command = new NpgsqlCommand("""
            SELECT hunk_position, position FROM public_api.document_change_word_edits
             WHERE record_id = @id AND revision_number = 2 ORDER BY hunk_position, position
            """, connection);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(int, int)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return rows;
    }

    private async Task<List<(int Position, string ExtractionId, int Start, int Length)>> ReadCitationsAsync(string recordId)
    {
        await using var connection = await OpenViewsAsync();
        await using var command = new NpgsqlCommand("""
            SELECT position, extraction_id, citation_start, citation_length
              FROM public_api.document_change_citations
             WHERE record_id = @id AND revision_number = 2 ORDER BY position
            """, connection);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(int, string, int, int)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        return rows;
    }

    private async Task<NpgsqlConnection> OpenViewsAsync()
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SET ROLE civic_lens_public", connection);
            await command.ExecuteNonQueryAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<int> ReadStateVersionAsync(string draftId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT review_state_version FROM document_change_drafts WHERE draft_id = @id", connection);
        command.Parameters.AddWithValue("id", draftId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task ActivateReleaseAsync(int release) =>
        await publications.ActivateAsync(release, (_, _) => Task.CompletedTask, default);

    private async Task SetHunkCountNullAsync(string comparisonId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE document_comparisons SET hunk_count = NULL WHERE comparison_id = @id", connection);
        command.Parameters.AddWithValue("id", comparisonId);
        await command.ExecuteNonQueryAsync();
    }

    private static string Hash(int value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
}
