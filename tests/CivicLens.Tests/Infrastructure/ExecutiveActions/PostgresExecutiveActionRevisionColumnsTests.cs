using System.Data.Common;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveActionRevisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Correction = "R1-2024-26714";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-action-columns-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ExecutiveActionSeeder executiveActions = null!;

    [Fact]
    public async Task OriginalDispositionNotesAndTheirCitationSurviveCorrectionRevisions()
    {
        const string notes = "Amends: EO 13224, September 23, 2001\r\nSee: EO 14193, February 1, 2025";
        var recordId = await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamation, notes), "joe-biden");
        var original = (await ReviewStore().GetAsync(recordId, default))!.Review.CurrentRevision.DispositionNotes;
        Assert.NotNull(original);

        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamationCorrection, "Correction-specific notes"), "joe-biden");
        var read = (await ReviewStore().GetAsync(recordId, default))!;
        Assert.Equal(2, read.Review.CurrentRevision.RevisionNumber);
        Assert.Equal(original, read.Review.CurrentRevision.DispositionNotes);
        Assert.Equal(notes, read.Review.CurrentRevision.DispositionNotes!.Value);
        var citation = original.Citation;
        Assert.Equal(original.Text, read.Extractions[citation.ExtractionId].Text.Substring(citation.Start, citation.Length));
        var verification = await VerifyAsync();
        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task LegacyColumnsRecoverCitedNotesFromTheirOwnRevisionWithoutChangingJson()
    {
        const string notes = "Revokes: EO 14056, December 1, 2021\r\nSee: EO 14186, January 27, 2025";
        var recordId = await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, notes), "donald-trump");
        var original = (await ReviewStore().GetAsync(recordId, default))!.Review.CurrentRevision.DispositionNotes;
        var json = await ScalarAsync("SELECT revision_json FROM executive_action_revisions");
        await executiveActions.MakeLegacyAsync();

        var result = await BackfillAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM executive_action_revisions"));
        Assert.Equal(original, (await ReviewStore().GetAsync(recordId, default))!.Review.CurrentRevision.DispositionNotes);
        Assert.Equal(0, (await VerifyAsync()).Mismatched);
    }

    [Fact]
    public async Task APreNotesJsonRevisionStillVerifiesWhenTheOptionalFieldIsAbsent()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await ExecuteAsync("UPDATE executive_action_revisions SET revision_json = (revision_json::jsonb - 'dispositionNotes')::text");

        var verification = await VerifyAsync();

        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task CorruptDispositionCitationsFailClosedWithoutJsonFallback()
    {
        var recordId = await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, "A cited note"), "donald-trump");
        await ExecuteAsync("""
            UPDATE executive_action_revisions SET disposition_notes_length = disposition_notes_length + 1
            """);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => ReviewStore().GetAsync(recordId, default));
    }

    private static string WithNotes(string json, string notes) =>
        json.Replace("\"html_url\":", $"\"disposition_notes\":{JsonSerializer.Serialize(notes)},\"html_url\":", StringComparison.Ordinal);

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        executiveActions = new ExecutiveActionSeeder(connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched()
    {
        await SeedCorrectedProclamationAsync();
        await executiveActions.MakeLegacyAsync();
        var jsonBefore = await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM executive_action_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((2, 2, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM executive_action_revisions"));
        Assert.Equal(18L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_facts"));
        Assert.Equal(Correction, await ScalarAsync("SELECT document_number FROM executive_action_revision_notices"));
        var verification = await VerifyAsync();
        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns()
    {
        await SeedCorrectedProclamationAsync();
        await executiveActions.MakeLegacyAsync();
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
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();
        await ExecuteAsync("UPDATE executive_action_revisions SET revision_json = @json", ("json", json));

        var result = await BackfillAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM executive_action_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_facts"));
    }

    [Fact]
    public async Task AnOldRevisionThatFailsTheShapeRulesOfTodayIsSkippedAndNeverChanged()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();
        await ExecuteAsync("UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{personId}', '\"\"')::text");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Null(await ScalarAsync("SELECT source_id FROM executive_action_revisions"));
    }

    [Theory]
    [InlineData("""UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{facts,0,citation,extractionId}', to_jsonb(repeat('f', 64)))::text""",
        "FK_executive_action_revision_facts_document_extractions")]
    [InlineData("""UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{notices,0,documentNumber}', '"R1-2000-00001"')::text WHERE revision_number = 2""",
        "FK_executive_action_revision_notices_executive_action_document")]
    public async Task AnIdThatDanglesIsSkippedAndNothingIsGuessedBecauseAForeignKeyRefusesIt(string change, string constraintPart)
    {
        await SeedCorrectedProclamationAsync();
        await executiveActions.MakeLegacyAsync();
        await ExecuteAsync(change);

        var result = await BackfillAsync(10);

        Assert.Contains(result.SkippedRevisions, revision => revision.Reason.StartsWith("database:", StringComparison.Ordinal) &&
            revision.Reason.Contains(constraintPart, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARowWhoseNoticeDocumentDiffersFromItsJsonIsSkippedWithNoTraceAndTheRowsAroundItStillMove()
    {
        await SeedCorrectedProclamationAsync();
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();
        await ExecuteAsync("UPDATE executive_action_documents SET notice_url = 'https://example.test/other' WHERE document_number = @number", ("number", Correction));

        var result = await BackfillAsync(10);

        Assert.Equal((3, 2, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal("RebuildMismatch", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions WHERE source_id IS NULL"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_notices"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_facts f JOIN executive_action_revisions r USING (record_id, revision_number) WHERE r.source_id IS NULL"));
    }

    [Fact]
    public async Task TheBackfillWaitsForTheExecutiveActionReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-executive-action-review");

        var running = BackfillAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-executive-action-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE executive_action_revisions SET title = 'Private changed title'", "title")]
    [InlineData("UPDATE executive_action_revisions SET document_url = 'https://private.example/x'", "documentUrl")]
    [InlineData("UPDATE executive_action_revisions SET kind = 'Notice'", "kind")]
    [InlineData("UPDATE executive_action_revisions SET person_id = 'private-person'", "personId")]
    [InlineData("UPDATE executive_action_revisions SET source_id = 'private-source'", "sourceId")]
    [InlineData("UPDATE executive_action_revisions SET presidential_number = '1'", "presidentialNumber")]
    [InlineData("UPDATE executive_action_revisions SET signed_on = signed_on + 1", "signedOn")]
    [InlineData("UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{authorSubject}', '\"auth0|private\"')::text", "authorSubject")]
    [InlineData("UPDATE executive_action_revision_facts SET citation_start = citation_start + 1", "facts")]
    [InlineData("DELETE FROM executive_action_revision_notices", "notices")]
    [InlineData("UPDATE executive_action_documents SET notice_url = 'https://private.example/x' WHERE notice_url IS NOT NULL", "notices")]
    [InlineData("DELETE FROM executive_action_revision_facts WHERE position = 0", "columns")]
    [InlineData("UPDATE executive_action_documents SET notice_url = NULL, notice_citation_extraction_id = NULL, notice_citation_start = NULL, notice_citation_length = NULL WHERE notice_url IS NOT NULL", "columns")]
    [InlineData("UPDATE executive_action_documents SET record_id = repeat('a', 64) WHERE notice_url IS NOT NULL", "columns")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText(string change, string field)
    {
        await SeedCorrectedProclamationAsync();
        await ExecuteAsync("SET session_replication_role = replica;\n" + change);

        var verification = await VerifyAsync();

        Assert.True(verification.Mismatched >= 1);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await ExecuteAsync("UPDATE executive_action_revisions SET revision_json = 'x'");
        var snapshot = await SnapshotAsync();

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Fact]
    public async Task AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => ReviewStore().GetAsync(recordId, default));

        Assert.Contains("Run the executive action revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeadlineSearchFailsOnARowWithNullColumnsBecauseItMustNotSkipTheRow()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => QueueStore().SearchApprovedAsync("weaponization", null, 10, default));

        Assert.Contains("executive action", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeadlineSearchReadsTheTitleColumnAndNotTheJson()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await ExecuteAsync("UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{title}', '\"Zebra json\"')::text");

        var hits = await QueueStore().SearchApprovedAsync("zebra", null, 10, default);
        var titled = await QueueStore().SearchApprovedAsync("weaponization", null, 10, default);

        Assert.Empty(hits.Items);
        Assert.Equal(recordId, Assert.Single(titled.Items).RecordId);
    }

    [Fact]
    public async Task AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await ExecuteAsync("UPDATE executive_action_revisions SET person_id = 'column-person', revision_json = jsonb_set(revision_json::jsonb, '{personId}', '\"json-person\"')::text");

        var review = await ReviewStore().GetAsync(recordId, default);

        Assert.Equal("column-person", review!.Review.CurrentRevision.PersonId);
    }

    [Fact]
    public async Task ReadingMoreRecordsCostsTheSameNumberOfQueriesBecauseNoReaderQueriesEachRow()
    {
        var counter = new QueryCounter();
        var counted = new PostgresExecutiveActionReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options), TestCodeVersion.Current);
        var first = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await counted.GetForPublicationAsync([first], default);
        counter.Count = 0;
        await counted.GetForPublicationAsync([first], default);
        var queriesForOne = counter.Count;
        foreach (var number in Enumerable.Range(1, 3))
            await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder.Replace("2025-01900", $"2025-0190{number}"), "donald-trump");
        var all = (await ScalarAsync("SELECT string_agg(record_id, ',') FROM executive_action_drafts") as string)!.Split(',');
        counter.Count = 0;

        var read = await counted.GetForPublicationAsync([.. all], default);

        Assert.Equal(4, read.Count);
        Assert.Equal(queriesForOne, counter.Count);
    }

    [Theory]
    [InlineData("UPDATE executive_action_revisions SET author_kind = 'Person'", "ck_executive_action_revisions_author")]
    [InlineData("UPDATE executive_action_revisions SET author_subject = 'auth0|person'", "ck_executive_action_revisions_author")]
    [InlineData("UPDATE executive_action_revisions SET title = NULL", "ck_executive_action_revisions_columns_written")]
    [InlineData("UPDATE executive_action_revisions SET kind = 'Treaty'", "ck_executive_action_revisions_kind")]
    [InlineData("UPDATE executive_action_revisions SET title = E'one\\ntwo'", "ck_executive_action_revisions_fields")]
    [InlineData("UPDATE executive_action_revisions SET document_url = '  '", "ck_executive_action_revisions_fields")]
    [InlineData("UPDATE executive_action_revisions SET presidential_number = ' '", "ck_executive_action_revisions_fields")]
    [InlineData("UPDATE executive_action_revisions SET person_id = ' '", "ck_executive_action_revisions_ids")]
    [InlineData("UPDATE executive_action_revisions SET signed_on = '0001-01-01'", "ck_executive_action_revisions_dates")]
    [InlineData("UPDATE executive_action_revision_facts SET citation_length = 0", "ck_executive_action_revision_facts_citation")]
    [InlineData("UPDATE executive_action_revision_facts SET position = -1 WHERE position = 0", "ck_executive_action_revision_facts_position")]
    [InlineData("UPDATE executive_action_revision_notices SET position = -1", "ck_executive_action_revision_notices_position")]
    [InlineData("UPDATE executive_action_documents SET notice_url = NULL WHERE notice_url IS NOT NULL", "ck_executive_action_documents_notice")]
    [InlineData("UPDATE executive_action_documents SET notice_url = ' ' WHERE notice_url IS NOT NULL", "ck_executive_action_documents_notice")]
    [InlineData("UPDATE executive_action_documents SET notice_citation_length = 0 WHERE notice_url IS NOT NULL", "ck_executive_action_documents_notice_citation")]
    [InlineData("UPDATE executive_action_documents SET revision_number = NULL WHERE notice_url IS NULL", "ck_executive_action_documents_pending")]
    public async Task TheDatabaseRefusesARowThatBreaksARule(string change, string constraint)
    {
        await SeedCorrectedProclamationAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Theory]
    [InlineData("INSERT INTO executive_action_revision_notices (record_id, revision_number, position, source_id, document_number) SELECT record_id, revision_number, 1, source_id, document_number FROM executive_action_revision_notices", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE executive_action_revision_notices SET document_number = 'R1-2000-00001'", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM executive_action_documents WHERE notice_url IS NOT NULL", PostgresErrorCodes.RestrictViolation)]
    [InlineData("UPDATE executive_action_revision_facts SET citation_extraction_id = repeat('f', 64)", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE executive_action_documents SET notice_citation_extraction_id = repeat('f', 64) WHERE notice_url IS NOT NULL", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM executive_action_revisions WHERE revision_number = 2", PostgresErrorCodes.RestrictViolation)]
    public async Task TheDatabaseRefusesALinkThatBreaksAKey(string change, string state)
    {
        await SeedCorrectedProclamationAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(state, thrown.SqlState);
    }

    private async Task SeedCorrectedProclamationAsync()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");
    }

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(BackfillStore()).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(BackfillStore()).ExecuteAsync(Owner);

    private PostgresExecutiveActionRevisionBackfillStore BackfillStore() => new(ContextFactory());

    private PostgresExecutiveActionReviewStore ReviewStore() => PostgresExecutiveActionReviewStore.FromConnectionString(connectionString, TestCodeVersion.Current);

    private PostgresReviewQueueStore QueueStore() => new(ContextFactory());

    private IDbContextFactory<CollectionAttemptDbContext> ContextFactory() => new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task<List<string>> SnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "executive_action_revisions", "executive_action_revision_facts", "executive_action_revision_notices", "executive_action_documents" })
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

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
