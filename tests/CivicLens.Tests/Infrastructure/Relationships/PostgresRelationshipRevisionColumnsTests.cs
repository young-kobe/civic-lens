using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Relationships;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRelationshipRevisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string AllColumns = """
        statement_candidate_id = NULL, statement_revision_number = NULL, target_kind = NULL, target_action_record_id = NULL,
        target_action_revision_number = NULL, target_vote_record_id = NULL, target_vote_revision_number = NULL, match_basis = NULL,
        match_extraction_id = NULL, match_start = NULL, match_length = NULL, type = NULL, reason = NULL, statement_quote_extraction_id = NULL,
        statement_quote_start = NULL, statement_quote_length = NULL, target_fact_id = NULL, official_id = NULL
        """;

    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);

    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresRelationshipDraftStore drafts = null!;
    private PostgresRelationshipReviewStore reviews = null!;
    private RelationshipWorldSeeder seeder = null!;
    private string recordId = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        drafts = new PostgresRelationshipDraftStore(factory);
        reviews = new PostgresRelationshipReviewStore(factory);
        seeder = new RelationshipWorldSeeder(connectionString);
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        recordId = ActionPair().PairId;
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheAnalysisWriteAndThePersonWriteStoreColumnsThatRebuildTheirRevisionsBecauseBothPathsMustFillThemTogetherWithTheJson()
    {
        await DraftAsync();
        var saved = await new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer,
            new(recordId, 1, RelationshipType.ClaimsCreditFor, "She claims credit for the bill.", Span(BillSentence), "F1", "save-2"));

        var verification = await VerifyAsync();

        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal(saved, await reviews.GetRevisionAsync(recordId, 2, default));
        Assert.Equal([$"Action|{ActionId}|1|<null>|<null>|{StatementId}|1", $"Action|{ActionId}|1|<null>|<null>|{StatementId}|1"], await ReadAsync("""
            SELECT target_kind || '|' || target_action_record_id || '|' || target_action_revision_number || '|' || coalesce(target_vote_record_id, '<null>')
                || '|' || coalesce(target_vote_revision_number::text, '<null>') || '|' || statement_candidate_id || '|' || statement_revision_number
            FROM relationship_revisions ORDER BY revision_number
            """));
    }

    [Fact]
    public async Task AVoteTargetFillsTheVoteColumnsAndLeavesTheActionColumnsNullBecauseExactlyOneTargetKeyIsSet()
    {
        await seeder.VoteAsync(Vote());
        var revision = Revision(VotePair()) with { Type = RelationshipType.StatementAboutVote, TargetFactId = "F1" };
        await seeder.RevisionAsync(revision);

        var verification = await VerifyAsync();

        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal(revision, await reviews.GetRevisionAsync(revision.RecordId, 1, default));
        Assert.Equal([$"Vote|{VoteId}|<null>"], await ReadAsync(
            "SELECT target_kind || '|' || target_vote_record_id || '|' || coalesce(target_action_record_id, '<null>') FROM relationship_revisions"));
    }

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched()
    {
        await DraftAsync();
        await MakeLegacyAsync();
        var jsonBefore = await ReadAsync("SELECT revision_json FROM relationship_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(jsonBefore, await ReadAsync("SELECT revision_json FROM relationship_revisions"));
        var verification = await VerifyAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns()
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await BackfillAsync(10);
        var snapshot = await ReadAsync("SELECT to_jsonb(r)::text FROM relationship_revisions r");

        var rerun = await BackfillAsync(10);

        Assert.Equal((0, 0, 0), (rerun.Examined, rerun.Moved, rerun.Skipped));
        Assert.Equal(snapshot, await ReadAsync("SELECT to_jsonb(r)::text FROM relationship_revisions r"));
    }

    [Theory]
    [InlineData("x", "UnreadableJson")]
    [InlineData("null", "UnreadableJson")]
    [InlineData("{}", "InvalidShape")]
    [InlineData("""{"recordId":"x","match":null}""", "InvalidShape")]
    public async Task JsonThatIsNoRevisionIsSkippedWithAReasonAndTheRowStaysAsItWas(string json, string reason)
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE relationship_revisions SET revision_json = @json", ("json", json));

        var result = await BackfillAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal([json], await ReadAsync("SELECT revision_json FROM relationship_revisions"));
        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(reason, '<null>') FROM relationship_revisions"));
    }

    [Fact]
    public async Task AnOldRevisionThatFailsTheShapeRulesOfTodayIsSkippedAndNeverChanged()
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync("UPDATE relationship_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{reason}', '\"one\\ntwo\"')::text");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(statement_candidate_id, '<null>') FROM relationship_revisions"));
    }

    [Theory]
    [InlineData("""UPDATE relationship_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{statement,revisionNumber}', '9')::text""",
        "FK_relationship_revisions_statement_revisions")]
    [InlineData("""UPDATE relationship_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{target,revisionNumber}', '9')::text""",
        "FK_relationship_revisions_action_revisions")]
    public async Task ACitationThatDanglesIsSkippedAndNothingIsGuessedBecauseAKeyRefusesIt(string change, string constraintPart)
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await ExecuteAsync(change);

        var result = await BackfillAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains(constraintPart, skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(statement_candidate_id, '<null>') FROM relationship_revisions"));
    }

    [Fact]
    public async Task TheBackfillWaitsForTheRelationshipReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch()
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");

        var running = BackfillAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await DraftAsync();
        await MakeLegacyAsync();

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE relationship_revisions SET reason = 'Private changed reason'", "reason")]
    [InlineData("UPDATE relationship_revisions SET target_fact_id = 'F9'", "targetFactId")]
    [InlineData("UPDATE relationship_revisions SET official_id = 'private-official'", "officialId")]
    [InlineData("UPDATE relationship_revisions SET type = 'StatesPositionOn'", "type")]
    [InlineData("UPDATE relationship_revisions SET match_basis = 'BillTitle'", "match")]
    [InlineData("UPDATE relationship_revisions SET match_start = match_start + 1, statement_quote_length = statement_quote_length + 1", "match")]
    [InlineData("UPDATE relationship_revisions SET statement_quote_start = statement_quote_start - 1, statement_quote_length = statement_quote_length + 1", "statementQuote")]
    [InlineData("UPDATE relationship_revisions SET statement_revision_number = 2", "statement")]
    [InlineData("UPDATE relationship_revisions SET target_action_revision_number = 2", "target")]
    [InlineData("UPDATE relationship_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{authorSubject}', '\"auth0|private\"')::text", "authorSubject")]
    [InlineData("UPDATE relationship_revisions SET created_at_utc_ticks = created_at_utc_ticks + 1", "createdAtUtc")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText(string change, string field)
    {
        await DraftAsync();
        await seeder.StatementAsync(Statement(revision: 2));
        await seeder.ActionAsync(Action(revision: 2));
        await ExecuteAsync("SET session_replication_role = replica;\n" + change);

        var verification = await VerifyAsync();

        Assert.True(verification.Mismatched >= 1);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await DraftAsync();
        await ExecuteAsync("UPDATE relationship_revisions SET revision_json = 'x'");
        var snapshot = await ReadAsync("SELECT to_jsonb(r)::text FROM relationship_revisions r");

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await ReadAsync("SELECT to_jsonb(r)::text FROM relationship_revisions r"));
    }

    [Fact]
    public async Task AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback()
    {
        await DraftAsync();
        await MakeLegacyAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => reviews.GetAsync(recordId, default));

        Assert.Contains("Run the relationship revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson()
    {
        await DraftAsync();
        await ExecuteAsync("UPDATE relationship_revisions SET reason = 'Column reason.', revision_json = jsonb_set(revision_json::jsonb, '{reason}', '\"Json reason.\"')::text");

        var revision = await reviews.GetRevisionAsync(recordId, 1, default);

        Assert.Equal("Column reason.", revision!.Reason);
    }

    [Fact]
    public async Task TheStaleCheckReadsTheTargetRevisionFromTheColumnsAndNotFromTheJson()
    {
        await DraftAsync();
        await ExecuteAsync("SET session_replication_role = replica;\nUPDATE relationship_revisions SET target_action_revision_number = 2");
        await seeder.ActionAsync(Action(revision: 2));

        await drafts.RefreshPairsAsync(default);

        Assert.Equal(["Succeeded"], await ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task TheStaleCheckSkipsARowWithNullColumnsBecauseOnlyTheBackfillMayTellItsTarget()
    {
        await DraftAsync();
        await MakeLegacyAsync();
        await seeder.ActionAsync(Action(revision: 2));

        await drafts.RefreshPairsAsync(default);

        Assert.Equal(["Succeeded"], await ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task ReadRevisionJsonRefusesTheRelationshipKindBecauseItIsReadFromItsColumns()
    {
        await using var db = await factory.CreateDbContextAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Review.CitedRevisions", "ReadRevisionJsonAsync",
            db, PublishedRecordKind.Relationship, new[] { new PublishedRevisionBinding(PublishedRecordKind.Relationship, "any", 1) }, CancellationToken.None));
    }

    [Theory]
    [InlineData("UPDATE relationship_revisions SET reason = NULL", "ck_relationship_revisions_columns_written")]
    [InlineData("UPDATE relationship_revisions SET reason = E'one\\ntwo'", "ck_relationship_revisions_reason")]
    [InlineData("UPDATE relationship_revisions SET reason = '   '", "ck_relationship_revisions_reason")]
    [InlineData("UPDATE relationship_revisions SET target_fact_id = ' '", "ck_relationship_revisions_fact_id")]
    [InlineData("UPDATE relationship_revisions SET official_id = ' '", "ck_relationship_revisions_official_id")]
    [InlineData("UPDATE relationship_revisions SET type = 'Nonsense'", "ck_relationship_revisions_type")]
    [InlineData("UPDATE relationship_revisions SET type = 'StatementAboutVote'", "ck_relationship_revisions_type_target")]
    [InlineData("UPDATE relationship_revisions SET match_basis = 'Guess'", "ck_relationship_revisions_match_basis")]
    [InlineData("UPDATE relationship_revisions SET target_kind = 'Statement'", "ck_relationship_revisions_target")]
    [InlineData("UPDATE relationship_revisions SET target_kind = 'Vote'", "ck_relationship_revisions_target")]
    [InlineData("UPDATE relationship_revisions SET target_vote_record_id = repeat('b', 64), target_vote_revision_number = 1", "ck_relationship_revisions_target")]
    [InlineData("UPDATE relationship_revisions SET target_action_record_id = NULL", "ck_relationship_revisions_target")]
    [InlineData("UPDATE relationship_revisions SET target_action_revision_number = 0", "ck_relationship_revisions_target_ids")]
    [InlineData("UPDATE relationship_revisions SET statement_revision_number = 0", "ck_relationship_revisions_statement")]
    [InlineData("UPDATE relationship_revisions SET statement_candidate_id = 'xyz'", "ck_relationship_revisions_statement")]
    [InlineData("UPDATE relationship_revisions SET statement_quote_length = 4097", "ck_relationship_revisions_quote")]
    [InlineData("UPDATE relationship_revisions SET statement_quote_start = match_start + 1", "ck_relationship_revisions_quote")]
    [InlineData("UPDATE relationship_revisions SET statement_quote_length = match_start - statement_quote_start + match_length - 1", "ck_relationship_revisions_quote")]
    [InlineData("UPDATE relationship_revisions SET statement_quote_extraction_id = repeat('9', 64)", "ck_relationship_revisions_quote")]
    [InlineData("UPDATE relationship_revisions SET match_length = 0", "ck_relationship_revisions_match")]
    [InlineData("UPDATE relationship_revisions SET author_kind = 'Code'", "ck_relationship_revisions_author_kind")]
    public async Task TheDatabaseRefusesARowThatBreaksARule(string change, string constraint)
    {
        await DraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE relationship_revisions SET statement_revision_number = 9", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE relationship_revisions SET target_action_revision_number = 9", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE relationship_revisions SET match_extraction_id = repeat('9', 64), statement_quote_extraction_id = repeat('9', 64)", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM statement_revisions", PostgresErrorCodes.RestrictViolation)]
    [InlineData("DELETE FROM action_revisions", PostgresErrorCodes.RestrictViolation)]
    public async Task TheDatabaseRefusesALinkThatBreaksAKey(string change, string state)
    {
        await DraftAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(state, thrown.SqlState);
    }

    [Fact]
    public async Task TheDatabaseRefusesAVoteLinkToAMissingVoteRevisionAndAVoteRevisionThatARelationshipCites()
    {
        await seeder.VoteAsync(Vote());
        await seeder.RevisionAsync(Revision(VotePair()) with { Type = RelationshipType.StatementAboutVote, TargetFactId = "F1" });

        var missing = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE relationship_revisions SET target_vote_revision_number = 9"));
        var cited = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM vote_revisions"));

        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.RestrictViolation), (missing.SqlState, cited.SqlState));
    }

    private async Task DraftAsync()
    {
        _ = await drafts.RefreshPairsAsync(default);
        await AnalysisWorkers.RelationshipDrafting(drafts, new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task),
            new FakeRelationshipDraftModel().Draft(Output(quote: BillWords)), new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials,
            TimeProvider.System).ExecuteAsync(once: true);
    }

    private Task MakeLegacyAsync() => ExecuteAsync($"UPDATE relationship_revisions SET {AllColumns}");

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(new PostgresRelationshipRevisionBackfillStore(factory)).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(new PostgresRelationshipRevisionBackfillStore(factory)).ExecuteAsync(Owner);

    private Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => seeder.ExecuteAsync(sql, parameters);

    private async Task<List<string>> ReadAsync(string sql) => [.. (await seeder.ReadAsync(sql)).Select(row => row[0])];
}
