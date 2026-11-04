using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeRevisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DateOnly ChangeDate = new(2026, 10, 1);

    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "document-change-columns-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private ComparisonSeeder comparisons = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        Directory.CreateDirectory(captureRoot);
        comparisons = new ComparisonSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory),
            new PostgresDocumentComparisonStore(factory), captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ASavedRevisionWritesItsColumnsAndChildRowsBesideTheJsonAndTheOrderOfEachListSurvives()
    {
        var saved = await SaveFilledDraftAsync();

        Assert.Equal(["auth0|owner|Person|Fee rule|The fee rose.|Why it matters|What is unknown|Example Office|2026-10-01"],
            await ReadAsync("""
                SELECT author_subject || '|' || author_kind || '|' || headline || '|' || summary || '|' || significance || '|' || limits || '|' || institution || '|' || change_date
                FROM document_change_revisions WHERE revision_number = 2
                """));
        Assert.Equal(["official-z,official-a|issue-z,issue-a"], await ReadAsync("""
            SELECT (SELECT string_agg(official_id, ',' ORDER BY position) FROM document_change_revision_officials o WHERE o.draft_id = r.draft_id AND o.revision_number = r.revision_number)
                || '|' || (SELECT string_agg(issue_id, ',' ORDER BY position) FROM document_change_revision_issues i WHERE i.draft_id = r.draft_id AND i.revision_number = r.revision_number)
            FROM document_change_revisions r WHERE revision_number = 2
            """));
        Assert.Equal((long)saved.Citations.Length, await ScalarAsync("SELECT count(*) FROM document_change_revision_citations WHERE revision_number = 2"));
        Assert.Equal(saved.ChangeDateEvidence!.Start, (int)(await ScalarAsync("SELECT citation_start FROM document_change_revision_citations WHERE revision_number = 2 AND is_change_date_evidence"))!);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM document_change_revision_citations WHERE revision_number = 2 AND is_change_date_evidence"));
        Assert.Equal("Fee rule", JsonDocument.Parse((string)(await ScalarAsync("SELECT revision_json FROM document_change_revisions WHERE revision_number = 2"))!)
            .RootElement.GetProperty("headline").GetString());
        Assert.Equal(Json(saved), Json((await reviews.GetAsync(saved.DraftId, default))!.CurrentRevision));
    }

    [Fact]
    public async Task AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson()
    {
        var saved = await SaveFilledDraftAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = 'not json'");

        var review = await reviews.GetAsync(saved.DraftId, default);

        Assert.Equal(Json(saved), Json(review!.CurrentRevision));
    }

    [Fact]
    public async Task AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback()
    {
        var saved = await SaveFilledDraftAsync();
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => reviews.GetAsync(saved.DraftId, default));

        Assert.Contains("Run the document change revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched()
    {
        var saved = await SaveFilledDraftAsync();
        await MakeLegacyAsync();
        var jsonBefore = await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM document_change_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((2, 2, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM document_change_revisions"));
        Assert.Equal(Json(saved), Json((await reviews.GetAsync(saved.DraftId, default))!.CurrentRevision));
        var verification = await VerifyAsync();
        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns()
    {
        await SaveFilledDraftAsync();
        await MakeLegacyAsync();
        await BackfillAsync(10);
        var snapshot = await SnapshotAsync();

        var rerun = await BackfillAsync(10);

        Assert.Equal((0, 0, 0), (rerun.Examined, rerun.Moved, rerun.Skipped));
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Theory]
    [InlineData("x", "UnreadableJson")]
    [InlineData("null", "UnreadableJson")]
    [InlineData("{}", "InvalidShape")]
    public async Task JsonThatIsNoRevisionIsSkippedWithAReasonAndTheRowStaysAsItWas(string json, string reason)
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = @json", ("json", json));

        var result = await BackfillAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM document_change_revisions"));
        Assert.Null(await ScalarAsync("SELECT headline FROM document_change_revisions"));
    }

    [Fact]
    public async Task ARevisionThatBreaksTheShapeRulesOfTodayIsSkippedAndNeverChanged()
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{officialIds}', to_jsonb(ARRAY['a', 'a']))::text");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM document_change_revision_officials"));
    }

    [Fact]
    public async Task ACitationThatDanglesIsSkippedBecauseAForeignKeyRefusesIt()
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{citations,0,extractionId}', to_jsonb(repeat('f', 64)))::text");

        var result = await BackfillAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains("FK_document_change_revision_citations_document_extractions", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARevisionThatNamesAnotherComparisonThanItsDraftIsSkippedBecauseTheCompositeKeyRefusesIt()
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{comparisonId}', to_jsonb(repeat('f', 64)))::text");

        var result = await BackfillAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains("FK_document_change_revisions_document_change_drafts_draft_id_c", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBackfillWaitsForTheDocumentChangeReviewLockBecauseAWriterMustNeverSeeAHalfMovedBatch()
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-document-change-review");

        var running = BackfillAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-document-change-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await CreateDraftAsync();
        await MakeLegacyAsync();

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE document_change_revisions SET headline = 'Private changed headline' WHERE revision_number = 2", "headline")]
    [InlineData("UPDATE document_change_revisions SET summary = 'Private changed summary' WHERE revision_number = 2", "summary")]
    [InlineData("UPDATE document_change_revisions SET significance = NULL WHERE revision_number = 2", "significance")]
    [InlineData("UPDATE document_change_revisions SET limits = 'Private limits' WHERE revision_number = 2", "limits")]
    [InlineData("UPDATE document_change_revisions SET institution = 'Private office' WHERE revision_number = 2", "institution")]
    [InlineData("UPDATE document_change_revisions SET change_date = change_date + 1 WHERE revision_number = 2", "changeDate")]
    [InlineData("UPDATE document_change_revisions SET author_subject = 'auth0|private' WHERE revision_number = 2", "authorSubject")]
    [InlineData("UPDATE document_change_revisions SET created_at_utc_ticks = created_at_utc_ticks + 1 WHERE revision_number = 2", "createdAtUtc")]
    [InlineData("DELETE FROM document_change_revision_officials WHERE position = 0", "officialIds")]
    [InlineData("UPDATE document_change_revision_issues SET position = 1 - position", "issueIds")]
    [InlineData("UPDATE document_change_revision_citations SET citation_start = citation_start + 1 WHERE revision_number = 2 AND position = 0", "citations")]
    [InlineData("UPDATE document_change_revision_citations SET is_change_date_evidence = false WHERE is_change_date_evidence", "changeDateEvidence")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText(string change, string field)
    {
        await SaveFilledDraftAsync();
        await ExecuteAsync("SET session_replication_role = replica;\n" + change);

        var verification = await VerifyAsync();

        Assert.True(verification.Mismatched >= 1);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await CreateDraftAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = 'x'");
        var snapshot = await SnapshotAsync();

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Fact]
    public async Task TheHeadlineSearchReadsTheHeadlineColumnAndNotTheJson()
    {
        var draftId = await SeedApprovedDraftAsync("Zebra change");
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '\"Giraffe json\"')::text");

        var json = await QueueStore().SearchApprovedAsync("giraffe", null, 10, default);
        var column = await QueueStore().SearchApprovedAsync("zebra", null, 10, default);

        Assert.Empty(json.Items);
        Assert.Equal(draftId, Assert.Single(column.Items).RecordId);
    }

    [Fact]
    public async Task TheHeadlineSearchFailsOnARowWithNullColumnsBecauseItMustNotSkipTheRow()
    {
        await SeedApprovedDraftAsync("Zebra change");
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => QueueStore().SearchApprovedAsync("zebra", null, 10, default));

        Assert.Contains("document change", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActivityFeedReadsTheAuthorAndHeadlineFromTheColumnsAndNotFromTheJson()
    {
        var saved = await SaveFilledDraftAsync();
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = 'x'");

        var events = await reviews.ListRecentActivityAsync(10, default);

        Assert.Contains(events, activity => activity.Kind == ReviewActivityKind.RevisionSaved && activity.Headline == "Fee rule" &&
            activity.ActorSubject == "auth0|owner" && activity.DraftId == saved.DraftId);
        Assert.Contains(events, activity => activity.Kind == ReviewActivityKind.DraftCreated && activity.Headline == "");
    }

    [Fact]
    public async Task TheActivityFeedFailsOnARowWithNullColumnsAndNamesTheBackfill()
    {
        await SaveFilledDraftAsync();
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => reviews.ListRecentActivityAsync(10, default));

        Assert.Contains("Run the document change revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBriefReaderBuildsACitedDocumentChangeFromTheColumnsAndNotFromTheJson()
    {
        var draftId = await SeedApprovedDraftAsync("Zebra change");
        await ExecuteAsync("UPDATE document_change_revisions SET revision_json = 'x'");
        await using var db = await factory.CreateDbContextAsync();

        var records = await ReadBriefRecordsAsync(db, [new(PublishedRecordKind.DocumentChange, draftId, 1)]);

        Assert.Equal("Zebra change", records.Single().GetType().GetProperty("Headline")!.GetValue(records.Single()));
    }

    [Fact]
    public async Task ReadRevisionJsonRefusesTheDocumentChangeKindBecauseItIsReadFromItsColumns()
    {
        await using var db = await factory.CreateDbContextAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Review.CitedRevisions", "ReadRevisionJsonAsync",
            db, PublishedRecordKind.DocumentChange, new[] { new PublishedRevisionBinding(PublishedRecordKind.DocumentChange, "any", 1) }, CancellationToken.None));
    }

    [Theory]
    [InlineData("UPDATE document_change_revisions SET author_kind = 'Robot'", "ck_document_change_revisions_author_kind")]
    [InlineData("UPDATE document_change_revisions SET comparison_id = 'NOT-HEX'", "ck_document_change_revisions_comparison")]
    [InlineData("UPDATE document_change_revisions SET summary = repeat('s', 8001)", "ck_document_change_revisions_summary")]
    [InlineData("UPDATE document_change_revisions SET significance = repeat('s', 4001)", "ck_document_change_revisions_significance")]
    [InlineData("UPDATE document_change_revisions SET limits = repeat('l', 4001)", "ck_document_change_revisions_limits")]
    [InlineData("UPDATE document_change_revisions SET headline = NULL", "ck_document_change_revisions_columns_written")]
    [InlineData("UPDATE document_change_revision_officials SET official_id = ' ' WHERE position = 0", "ck_document_change_revision_officials_official")]
    [InlineData("UPDATE document_change_revision_officials SET position = 64 WHERE position = 0", "ck_document_change_revision_officials_position")]
    [InlineData("UPDATE document_change_revision_issues SET issue_id = ' ' WHERE position = 0", "ck_document_change_revision_issues_issue")]
    [InlineData("UPDATE document_change_revision_citations SET position = 64 WHERE position = 0", "ck_document_change_revision_citations_position")]
    [InlineData("UPDATE document_change_revision_citations SET citation_length = 8193 WHERE position = 0", "ck_document_change_revision_citations_length")]
    [InlineData("UPDATE document_change_revision_citations SET citation_start = -1 WHERE position = 0", "ck_document_change_revision_citations_citation")]
    public async Task TheDatabaseRefusesARowThatBreaksARule(string change, string constraint)
    {
        await SaveFilledDraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Theory]
    [InlineData("INSERT INTO document_change_revision_citations SELECT draft_id, revision_number, 63, citation_extraction_id, citation_start, citation_length FROM document_change_revision_citations WHERE position = 0 AND revision_number = 2", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE document_change_revisions SET comparison_id = repeat('f', 64) WHERE revision_number = 2", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE document_change_revision_citations SET citation_extraction_id = repeat('f', 64) WHERE position = 0", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("INSERT INTO document_change_revision_officials SELECT draft_id, revision_number, official_id, 40 FROM document_change_revision_officials WHERE position = 0", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE document_change_revision_issues SET revision_number = 9", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM document_change_revisions WHERE revision_number = 2", PostgresErrorCodes.RestrictViolation)]
    public async Task TheDatabaseRefusesALinkThatBreaksAKey(string change, string state)
    {
        await SaveFilledDraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(state, thrown.SqlState);
    }

    [Fact]
    public async Task TheDatabaseRefusesASecondCitationFlaggedAsTheChangeDateEvidenceOfOneRevision()
    {
        await SaveFilledDraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "UPDATE document_change_revision_citations SET is_change_date_evidence = true WHERE revision_number = 2 AND NOT is_change_date_evidence AND position = 1"));

        Assert.Equal("ux_document_change_revision_citations_change_date_evidence", thrown.ConstraintName);
    }

    [Fact]
    public async Task TheCodeAuthorIsAllowedBecauseCodeWillWriteRevisionsOfThisKind()
    {
        await CreateDraftAsync();

        await ExecuteAsync("UPDATE document_change_revisions SET author_kind = 'Code', author_subject = 'civic-lens:code'");

        Assert.Equal("Code", await ScalarAsync("SELECT author_kind FROM document_change_revisions"));
    }

    private static string Json(DocumentChangeDraftRevision revision) => JsonSerializer.Serialize(revision, JsonSerializerOptions.Web);

    private async Task<DocumentChangeDraftRevision> CreateDraftAsync()
    {
        var comparison = await comparisons.SaveAsync("Policy before.", "Policy after.");
        return await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new(comparison.ComparisonId, "create-" + Guid.NewGuid().ToString("N")));
    }

    private async Task<DocumentChangeDraftRevision> SaveFilledDraftAsync()
    {
        var created = await CreateDraftAsync();
        var catalog = new ReviewCatalog(["issue-a", "issue-z"], ["official-a", "official-z"]);
        return await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(Owner, new(created.DraftId, 1, "Fee rule", "The fee rose.", "Why it matters",
            "What is unknown", "Example Office", ChangeDate, created.Citations[0], ["official-z", "official-a"], ["issue-z", "issue-a"], created.Citations, "save-" + created.DraftId));
    }

    private async Task<string> SeedApprovedDraftAsync(string headline)
    {
        var draftId = new string('d', 32);
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, repeat('c', 64), 1, 0, 'Approved', 0, 100)
            """, ("id", draftId));
        await new BriefWorldSeeder(connectionString).DocumentChangeAsync(draftId, 1, headline);
        return draftId;
    }

    private static async Task<IReadOnlyList<object>> ReadBriefRecordsAsync(CollectionAttemptDbContext db, ImmutableArray<PublishedRevisionBinding> bindings)
    {
        var reader = typeof(PostgresDocumentChangeReviewStore).Assembly.GetType("CivicLens.Infrastructure.Briefs.BriefRecordReader")!;
        var task = (Task)reader.GetMethod("ReadExactAsync")!.Invoke(null, [db, bindings, CancellationToken.None])!;
        await task;
        return [.. ((IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!).Cast<object>()];
    }

    private Task MakeLegacyAsync() => ExecuteAsync("""
        DELETE FROM document_change_revision_officials;
        DELETE FROM document_change_revision_issues;
        DELETE FROM document_change_revision_citations;
        UPDATE document_change_revisions SET comparison_id = NULL, headline = NULL, summary = NULL, significance = NULL, limits = NULL, institution = NULL,
            change_date = NULL
        """);

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(BackfillStore()).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(BackfillStore()).ExecuteAsync(Owner);

    private PostgresDocumentChangeRevisionBackfillStore BackfillStore() => new(factory);

    private PostgresReviewQueueStore QueueStore() => new(factory);

    private async Task<List<string>> SnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "document_change_revisions", "document_change_revision_officials", "document_change_revision_issues", "document_change_revision_citations" })
            snapshot.Add((string)(await ScalarAsync($"SELECT coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY to_jsonb(t)::text), '') FROM {table} t"))!);
        return snapshot;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }
}
