using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementRevisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Quote = "a rent cap for tenants";
    private const string SecondQuote = "This cap protects families";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DateOnly StatementDate = new(2026, 10, 3);

    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "statement-columns-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresStatementDraftStore drafts = null!;
    private PostgresStatementReviewStore reviews = null!;
    private StatementCandidateSeeder seeder = null!;
    private int candidateCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        drafts = new PostgresStatementDraftStore(factory);
        reviews = new PostgresStatementReviewStore(factory);
        Directory.CreateDirectory(captureRoot);
        seeder = new StatementCandidateSeeder(new PostgresCollectionAttemptStore(factory), new PostgresDocumentExtractionStore(factory), connectionString, captureRoot);
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

        Assert.Equal(["auth0|owner|Person|Warren cap|The senator announced a cap.|2026-10-03"], await ReadAsync("""
            SELECT author_subject || '|' || author_kind || '|' || headline || '|' || summary || '|' || statement_date
            FROM statement_revisions WHERE revision_number = 2
            """));
        Assert.Equal(["warren|Jo Rivera,Pat Smith|rent-affordability|health,housing-and-community-development|S. 1234,H.R. 56"], await ReadAsync("""
            SELECT (SELECT string_agg(official_id, ',' ORDER BY position) FROM statement_revision_speakers c WHERE c.candidate_id = r.candidate_id AND c.revision_number = r.revision_number)
                || '|' || (SELECT string_agg(speaker_name, ',' ORDER BY position) FROM statement_revision_other_speakers c WHERE c.candidate_id = r.candidate_id AND c.revision_number = r.revision_number)
                || '|' || (SELECT string_agg(issue_id, ',' ORDER BY position) FROM statement_revision_issues c WHERE c.candidate_id = r.candidate_id AND c.revision_number = r.revision_number)
                || '|' || (SELECT string_agg(policy_area_id, ',' ORDER BY position) FROM statement_revision_policy_areas c WHERE c.candidate_id = r.candidate_id AND c.revision_number = r.revision_number)
                || '|' || (SELECT string_agg(bill_number, ',' ORDER BY position) FROM statement_revision_bill_numbers c WHERE c.candidate_id = r.candidate_id AND c.revision_number = r.revision_number)
            FROM statement_revisions r WHERE revision_number = 2
            """));
        Assert.Equal(saved.Quotations.Select(quotation => $"{quotation.Span.Start}|{quotation.Span.Length}|{quotation.Kind}"),
            await ReadAsync("SELECT quotation_start || '|' || quotation_length || '|' || kind FROM statement_revision_quotations WHERE revision_number = 2 ORDER BY position"));
        Assert.Equal(saved.StatementDateEvidence!.Start, (int)(await ScalarAsync("SELECT statement_date_evidence_start FROM statement_revisions WHERE revision_number = 2"))!);
        Assert.Equal("Warren cap", JsonDocument.Parse((string)(await ScalarAsync("SELECT revision_json FROM statement_revisions WHERE revision_number = 2"))!)
            .RootElement.GetProperty("headline").GetString());
        Assert.Equal(Json(saved), Json((await reviews.GetAsync(saved.CandidateId, default))!.Review.CurrentRevision!));
    }

    [Fact]
    public async Task ARevisionWithoutADateWritesNoDateAndNoEvidence()
    {
        var candidate = await StartedAsync();
        await Save.ExecuteAsync(Owner, (await SaveRequestAsync(candidate, 1)) with { StatementDate = null, StatementDateEvidence = null });

        Assert.Equal(["<null>|<null>"], await ReadAsync(
            "SELECT coalesce(statement_date::text, '<null>') || '|' || coalesce(statement_date_evidence_extraction_id, '<null>') FROM statement_revisions WHERE revision_number = 2"));
    }

    [Fact]
    public async Task AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson()
    {
        var saved = await SaveFilledDraftAsync();
        await ExecuteAsync("UPDATE statement_revisions SET revision_json = 'not json'");

        var review = await reviews.GetAsync(saved.CandidateId, default);

        Assert.Equal(Json(saved), Json(review!.Review.CurrentRevision!));
    }

    [Fact]
    public async Task AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback()
    {
        var saved = await SaveFilledDraftAsync();
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => reviews.GetAsync(saved.CandidateId, default));

        Assert.Contains("Run the statement revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched()
    {
        var saved = await SaveFilledDraftAsync();
        await MakeLegacyAsync();
        var jsonBefore = await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM statement_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((2, 2, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT string_agg(revision_json, '|' ORDER BY revision_number) FROM statement_revisions"));
        Assert.Equal(Json(saved), Json((await reviews.GetAsync(saved.CandidateId, default))!.Review.CurrentRevision!));
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
        await StartedAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE statement_revisions SET revision_json = @json", ("json", json));

        var result = await BackfillAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM statement_revisions"));
        Assert.Null(await ScalarAsync("SELECT headline FROM statement_revisions"));
    }

    [Theory]
    [InlineData("speakerOfficialIds")]
    [InlineData("issueIds")]
    [InlineData("policyAreaIds")]
    public async Task ARevisionWithADuplicateConfigurationIdIsSkippedAndNeverChangedBecauseTheKeyAndTheShapeRuleRefuseIt(string field)
    {
        await SaveFilledDraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync($"UPDATE statement_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{{{field}}}', to_jsonb(ARRAY['a', 'a']))::text WHERE revision_number = 2");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_revision_speakers WHERE revision_number = 2"));
    }

    [Fact]
    public async Task AQuotationOfAnotherExtractionThanTheCandidateIsSkippedBecauseTheCompositeKeyRefusesIt()
    {
        await SaveFilledDraftAsync();
        await MakeLegacyAsync();
        var other = new string('f', 64);
        await ExecuteAsync("""
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            VALUES (@other, (SELECT attempt_id FROM statement_candidates LIMIT 1), 'extraction', 'other text', repeat('0', 64))
            """, ("other", other));
        await ExecuteAsync("UPDATE statement_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{quotations,0,span,extractionId}', to_jsonb(@other::text))::text WHERE revision_number = 2",
            ("other", other));

        var result = await BackfillAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains("statement_candidates", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBackfillWaitsForTheStatementReviewLockBecauseAWriterMustNeverSeeAHalfMovedBatch()
    {
        await StartedAsync();
        await MakeLegacyAsync();
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-statement-review");

        var running = BackfillAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-statement-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await StartedAsync();
        await MakeLegacyAsync();

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE statement_revisions SET headline = 'Private changed headline' WHERE revision_number = 2", "headline")]
    [InlineData("UPDATE statement_revisions SET summary = 'Private changed summary' WHERE revision_number = 2", "summary")]
    [InlineData("UPDATE statement_revisions SET statement_date = statement_date + 1 WHERE revision_number = 2", "statementDate")]
    [InlineData("UPDATE statement_revisions SET author_subject = 'auth0|private' WHERE revision_number = 2", "authorSubject")]
    [InlineData("UPDATE statement_revisions SET created_at_utc_ticks = created_at_utc_ticks + 1 WHERE revision_number = 2", "createdAtUtc")]
    [InlineData("DELETE FROM statement_revision_speakers WHERE position = 0", "speakerOfficialIds")]
    [InlineData("DELETE FROM statement_revision_other_speakers WHERE position = 1", "otherSpeakers")]
    [InlineData("UPDATE statement_revision_policy_areas SET position = 1 - position", "policyAreaIds")]
    [InlineData("DELETE FROM statement_revision_issues", "issueIds")]
    [InlineData("UPDATE statement_revision_quotations SET quotation_start = quotation_start + 1 WHERE revision_number = 2 AND position = 0", "quotations")]
    [InlineData("UPDATE statement_revision_quotations SET kind = 'DirectQuotation' WHERE kind = 'ReleaseText'", "quotations")]
    [InlineData("DELETE FROM statement_revision_bill_numbers WHERE position = 0", "billNumbers")]
    [InlineData("UPDATE statement_revisions SET statement_date = NULL, statement_date_evidence_extraction_id = NULL, statement_date_evidence_start = NULL, statement_date_evidence_length = NULL WHERE revision_number = 2", "statementDate")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText(string change, string field)
    {
        await SaveFilledDraftAsync();
        await ExecuteAsync(change);

        var verification = await VerifyAsync();

        Assert.True(verification.Mismatched >= 1);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await StartedAsync();
        await ExecuteAsync("UPDATE statement_revisions SET revision_json = 'x'");
        var snapshot = await SnapshotAsync();

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Fact]
    public async Task TheQueueHeadlineAndTheSearchReadTheHeadlineColumnAndNotTheJson()
    {
        var statement = RelationshipFixtures.Statement();
        await new RelationshipWorldSeeder(connectionString).StatementAsync(statement);
        await ExecuteAsync("UPDATE statement_revisions SET headline = 'Zebra statement', revision_json = jsonb_set(revision_json::jsonb, '{headline}', '\"Giraffe json\"')::text");

        var json = await QueueStore().SearchApprovedAsync("giraffe", null, 10, default);
        var column = await QueueStore().SearchApprovedAsync("zebra", null, 10, default);

        Assert.Empty(json.Items);
        Assert.Equal(statement.RecordId, Assert.Single(column.Items).RecordId);
    }

    [Fact]
    public async Task TheHeadlineSearchFailsOnARowWithNullColumnsBecauseItMustNotSkipTheRow()
    {
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.Statement());
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => QueueStore().SearchApprovedAsync("rent", null, 10, default));

        Assert.Contains("statement", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBillMentionsReadTheBillNumberRowsAndTheDateColumnAndNotTheJson()
    {
        var statement = RelationshipFixtures.Statement(date: new DateOnly(2026, 10, 3)) with { };
        var revision = statement.Revision with { BillNumbers = ["H.R. 1234"] };
        await new RelationshipWorldSeeder(connectionString).StatementAsync(statement with { Revision = revision });
        await ExecuteAsync("UPDATE statement_revisions SET revision_json = 'x'");

        var priority = await BillPriorityAsync(119, "hr", 1234);

        Assert.Equal(ReviewPriority.Highest, priority);
        Assert.Equal(ReviewPriority.Lowest, await BillPriorityAsync(119, "hr", 4321));
    }

    [Fact]
    public async Task TheBillMentionsFailOnARowWithNullColumnsBecauseAMissingMentionMustNotLowerAPriority()
    {
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.Statement());
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => BillPriorityAsync(119, "hr", 1234));

        Assert.Contains("Run the statement revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPDATE statement_revisions SET summary = repeat('s', 8001)", "ck_statement_revisions_summary")]
    [InlineData("UPDATE statement_revisions SET headline = NULL", "ck_statement_revisions_columns_written")]
    [InlineData("UPDATE statement_revisions SET statement_date = NULL WHERE revision_number = 2", "ck_statement_revisions_date_evidence")]
    [InlineData("UPDATE statement_revisions SET statement_date_evidence_length = 4097 WHERE revision_number = 2", "ck_statement_revisions_date_evidence_length")]
    [InlineData("UPDATE statement_revisions SET statement_date_evidence_start = -1 WHERE revision_number = 2", "ck_statement_revisions_statement_date_evidence")]
    [InlineData("UPDATE statement_revision_speakers SET official_id = ' ' WHERE position = 0", "ck_statement_revision_speakers_official")]
    [InlineData("UPDATE statement_revision_speakers SET position = 64 WHERE position = 0", "ck_statement_revision_speakers_position")]
    [InlineData("UPDATE statement_revision_other_speakers SET speaker_name = ' Jo Rivera' WHERE position = 0", "ck_statement_revision_other_speakers_name")]
    [InlineData("UPDATE statement_revision_other_speakers SET position = 16 WHERE position = 0", "ck_statement_revision_other_speakers_position")]
    [InlineData("UPDATE statement_revision_issues SET issue_id = ' ' WHERE position = 0", "ck_statement_revision_issues_issue")]
    [InlineData("UPDATE statement_revision_policy_areas SET policy_area_id = ' ' WHERE position = 0", "ck_statement_revision_policy_areas_policy_area")]
    [InlineData("UPDATE statement_revision_quotations SET kind = 'Rumor' WHERE position = 0", "ck_statement_revision_quotations_kind")]
    [InlineData("UPDATE statement_revision_quotations SET quotation_length = 4097 WHERE position = 0", "ck_statement_revision_quotations_length")]
    [InlineData("UPDATE statement_revision_quotations SET quotation_start = -1 WHERE position = 0", "ck_statement_revision_quotations_quotation")]
    [InlineData("UPDATE statement_revision_quotations SET position = 32 WHERE position = 0", "ck_statement_revision_quotations_position")]
    [InlineData("UPDATE statement_revision_bill_numbers SET bill_number = ' ' WHERE position = 0", "ck_statement_revision_bill_numbers_bill_number")]
    [InlineData("UPDATE statement_revision_bill_numbers SET position = -1 WHERE position = 0", "ck_statement_revision_bill_numbers_position")]
    public async Task TheDatabaseRefusesARowThatBreaksARule(string change, string constraint)
    {
        await SaveFilledDraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Theory]
    [InlineData("INSERT INTO statement_revision_quotations SELECT candidate_id, revision_number, 31, quotation_extraction_id, quotation_start, quotation_length, kind FROM statement_revision_quotations WHERE position = 0 AND revision_number = 2", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE statement_revision_quotations SET quotation_extraction_id = (SELECT extraction_id FROM document_extractions WHERE extraction_id <> (SELECT extraction_id FROM statement_candidates LIMIT 1) LIMIT 1) WHERE position = 0", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("INSERT INTO statement_revision_other_speakers SELECT candidate_id, revision_number, 9, speaker_name FROM statement_revision_other_speakers WHERE position = 0", PostgresErrorCodes.UniqueViolation)]
    [InlineData("INSERT INTO statement_revision_speakers SELECT candidate_id, revision_number, official_id, 40 FROM statement_revision_speakers WHERE position = 0", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE statement_revision_issues SET revision_number = 9", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM statement_revisions WHERE revision_number = 2", PostgresErrorCodes.RestrictViolation)]
    public async Task TheDatabaseRefusesALinkThatBreaksAKey(string change, string state)
    {
        await SaveFilledDraftAsync();
        await ExecuteAsync("""
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            VALUES (repeat('e', 64), (SELECT attempt_id FROM statement_candidates LIMIT 1), 'extraction', 'other', repeat('0', 64))
            """);

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(state, thrown.SqlState);
    }

    [Fact]
    public async Task TheDatabaseRefusesDateEvidenceFromAnotherExtractionThanTheCandidateHasBecauseOnlyItsArticleCanBeCited()
    {
        await SaveFilledDraftAsync();
        await ExecuteAsync("""
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            VALUES (repeat('e', 64), (SELECT attempt_id FROM statement_candidates LIMIT 1), 'extraction', 'other', repeat('0', 64))
            """);

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "UPDATE statement_revisions SET statement_date_evidence_extraction_id = repeat('e', 64) WHERE revision_number = 2"));

        Assert.Contains("statement_candidates", thrown.ConstraintName, StringComparison.Ordinal);
    }

    private static string Json(StatementDraftRevision revision) => JsonSerializer.Serialize(revision, JsonSerializerOptions.Web);

    private CreateStatementDraft Create => new(reviews);

    private SaveStatementDraft Save => new(reviews, new StatementReviewContextReader(drafts, new StatementSelectionResolver(StatementGateFixtures.Configuration())));

    private async Task<string> StartedAsync()
    {
        var candidate = await seeder.SeedAsync($"{Article} Release {++candidateCount}.", $"https://example.test/pages/{candidateCount}");
        await Create.ExecuteAsync(Owner, new(candidate, "create-" + candidate[..8]));
        return candidate;
    }

    private async Task<SaveStatementDraftRequest> SaveRequestAsync(string candidate, int expectedRevision)
    {
        var extraction = (await drafts.ReadEvidenceAsync(candidate, default))!.Article.Extraction;
        var quote = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf(Quote, StringComparison.Ordinal), Quote.Length);
        var second = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf(SecondQuote, StringComparison.Ordinal), SecondQuote.Length);
        var evidence = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf("October 3, 2026", StringComparison.Ordinal), "October 3, 2026".Length);
        return new(candidate, expectedRevision, "Warren cap", "The senator announced a cap.", StatementDate, evidence, [Warren],
            ["Jo Rivera", "Pat Smith"], [RentIssue], [HealthArea, HousingArea], [second, quote], "save-" + candidate[..8], StatementDateChecked: true);
    }

    private async Task<StatementDraftRevision> SaveFilledDraftAsync()
    {
        var candidate = await StartedAsync();
        return await Save.ExecuteAsync(Owner, await SaveRequestAsync(candidate, 1));
    }

    private async Task<int> BillPriorityAsync(int congress, string billType, int number)
    {
        await using var db = await factory.CreateDbContextAsync();
        var task = (Task<int>)typeof(PostgresStatementReviewStore).Assembly.GetType("CivicLens.Infrastructure.Statements.StatementBillMentions")!
            .GetMethod("PriorityOfBillAsync")!.Invoke(null, [db, congress, billType, number, false, CancellationToken.None])!;
        return await task;
    }

    private Task MakeLegacyAsync() => ExecuteAsync("""
        DELETE FROM statement_revision_speakers;
        DELETE FROM statement_revision_other_speakers;
        DELETE FROM statement_revision_issues;
        DELETE FROM statement_revision_policy_areas;
        DELETE FROM statement_revision_quotations;
        DELETE FROM statement_revision_bill_numbers;
        UPDATE statement_revisions SET headline = NULL, summary = NULL, statement_date = NULL, statement_date_evidence_extraction_id = NULL,
            statement_date_evidence_start = NULL, statement_date_evidence_length = NULL
        """);

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(BackfillStore()).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(BackfillStore()).ExecuteAsync(Owner);

    private PostgresStatementRevisionBackfillStore BackfillStore() => new(factory);

    private PostgresReviewQueueStore QueueStore() => new(factory);

    private async Task<List<string>> SnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "statement_revisions", "statement_revision_speakers", "statement_revision_other_speakers", "statement_revision_issues",
            "statement_revision_policy_areas", "statement_revision_quotations", "statement_revision_bill_numbers" })
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
