using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly ReviewActor BackfillOwner = new("auth0|owner", ReviewRole.Owner);

    private const string NullRevisionColumns = """
        headline = NULL, summary = NULL, text_origin = NULL, suggestion_headline = NULL, suggestion_summary = NULL, suggestion_run_id = NULL,
        suggestion_fingerprint = NULL, source_id = NULL, congress = NULL, bill_type = NULL, bill_number = NULL, candidate_id = NULL,
        candidate_update_utc_ticks = NULL, candidate_includes_summaries = NULL, candidate_includes_subjects = NULL, fingerprint = NULL,
        summary_version = NULL, summary_citation_extraction_id = NULL, summary_citation_start = NULL, summary_citation_length = NULL,
        policy_area = NULL, policy_area_citation_extraction_id = NULL, policy_area_citation_start = NULL, policy_area_citation_length = NULL,
        policy_area_id = NULL, bill_title = NULL, bill_citation_extraction_id = NULL, bill_citation_start = NULL, bill_citation_length = NULL,
        issue_revision_id = NULL, subjects_hash = NULL, issue_candidates_recorded = NULL, event_on = NULL, cosponsor_count = NULL
        """;

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouchedBecauseOnlyTheColumnsAreNew()
    {
        var full = await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        var jsonBefore = await ScalarAsync("SELECT revision_json FROM action_revisions");

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        await AssertColumnsEqualAsync(full);
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT revision_json FROM action_revisions"));
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARevisionWrittenWithoutTheDerivedValuesGetsThemFromTheBackfillBecauseEarlierCodeWroteTheOtherColumnsOnly()
    {
        var full = await SettleFullRevisionAsync();
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET event_on = NULL, cosponsor_count = NULL");
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions WHERE headline IS NOT NULL AND event_on IS NULL"));

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        await AssertColumnsEqualAsync(full);
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARevisionWithNoCandidateSetKeepsItsTrackedIssuesInOrderAndRecordsThatCandidatesAreUnknown()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1) with { IssueCandidateIds = default, TrackedIssueIds = ["issue-a", "issue-b"] });
        await MakeActionRevisionsLegacyAsync(1);

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal(1, result.Moved);
        Assert.Equal(["issue-a|true", "issue-b|true"], await LinesAsync("SELECT issue_id || '|' || is_tracked FROM action_revision_issues ORDER BY position"));
        Assert.Equal(0, (await VerifyActionRevisionsAsync()).Mismatched);
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithoutAnEventDate()
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        await BackfillActionRevisionsAsync(10);
        var snapshot = await ActionRevisionSnapshotAsync();

        var rerun = await BackfillActionRevisionsAsync(10);

        Assert.Equal((0, 0, 0), (rerun.Examined, rerun.Moved, rerun.Skipped));
        Assert.Equal(snapshot, await ActionRevisionSnapshotAsync());
    }

    [Fact]
    public async Task TheLimitStopsTheBackfillAfterThatManyMovedRowsBecauseTheOwnerPacesTheWrites()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await SaveAsync(1, SuggestionChoice.Keep, "limit-edit");
        await MakeActionRevisionsLegacyAsync(1, 2);

        var one = await BackfillActionRevisionsAsync(1);

        Assert.Equal(1, one.Moved);
        Assert.Equal([2], (await LinesAsync("SELECT revision_number::text FROM action_revisions WHERE headline IS NULL")).Select(int.Parse));
        var rest = await BackfillActionRevisionsAsync(1);
        Assert.Equal(1, rest.Moved);
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("x", "UnreadableJson")]
    [InlineData("null", "UnreadableJson")]
    [InlineData("{}", "InvalidShape")]
    public async Task JsonThatIsNoRevisionIsSkippedWithAReasonAndTheRowStaysAsItWas(string json, string reason)
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        await ExecuteAsync(connectionString, $"UPDATE action_revisions SET revision_json = '{json}'");

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM action_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_revision_sponsorships"));
    }

    [Fact]
    public async Task ARevisionWithMoreCitationsThanTheTableHoldsIsSkippedAsInvalidShapeAndNeverChanged()
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        await RewriteActionRevisionJsonAsync(1, json => json["sponsorships"]![1]!["citations"]!.AsArray().Add(json["sponsorships"]![1]!["citations"]![0]!.DeepClone()));

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions WHERE headline IS NULL"));
    }

    [Theory]
    [InlineData("sponsorships", "FK_action_revision_sponsorship_citations_document_extractions")]
    [InlineData("candidateId", "FK_action_revisions_action_candidates_candidate_id")]
    public async Task AnIdThatDanglesIsSkippedAndNothingIsGuessedBecauseAForeignKeyRefusesIt(string key, string constraintPart)
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        await RewriteActionRevisionJsonAsync(1, json =>
        {
            if (key == "candidateId") json[key] = new string('f', 64);
            else json[key]![0]!["citations"]![0]!["extractionId"] = new string('f', 64);
        });

        var result = await BackfillActionRevisionsAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains(constraintPart, skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_revision_sponsorships"));
    }

    [Fact]
    public async Task ARowThatFailsAfterItsWriteLeavesTheRowsAroundItMovedBecauseEachRowHasItsOwnSavepoint()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await SaveAsync(1, SuggestionChoice.Keep, "savepoint-edit");
        await MakeActionRevisionsLegacyAsync(1, 2);
        await RewriteActionRevisionJsonAsync(1, json => json["candidateId"] = new string('f', 64));

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((2, 1, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal([1], (await LinesAsync("SELECT revision_number::text FROM action_revisions WHERE headline IS NULL")).Select(int.Parse));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_revision_issues WHERE revision_number = 1"));
    }

    [Fact]
    public async Task TheBackfillWaitsForTheActionReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch()
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-action-review");

        var running = BackfillActionRevisionsAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-action-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);

        var verification = await VerifyActionRevisionsAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE action_revisions SET bill_title = 'Private changed title'", "billTitle")]
    [InlineData("UPDATE action_revisions SET fingerprint = repeat('f', 64)", "fingerprint")]
    [InlineData("UPDATE action_revisions SET candidate_includes_summaries = NOT candidate_includes_summaries", "candidateIncludesSummaries")]
    [InlineData("UPDATE action_revisions SET candidate_includes_subjects = false", "candidateIncludesSubjects")]
    [InlineData("UPDATE action_revisions SET policy_area = 'Private area'", "policyArea")]
    [InlineData("UPDATE action_revisions SET event_on = event_on + 1", "eventOn")]
    [InlineData("UPDATE action_revisions SET cosponsor_count = cosponsor_count + 1", "cosponsorCount")]
    [InlineData("UPDATE action_revision_issues SET is_tracked = NOT is_tracked WHERE issue_id = 'issue-a'", "trackedIssueIds")]
    [InlineData("DELETE FROM action_revision_issues WHERE issue_id = 'issue-c'", "issueCandidateIds")]
    [InlineData("UPDATE action_revision_sponsorships SET person_id = 'private-person' WHERE position = 0", "sponsorships")]
    [InlineData("UPDATE action_revision_sponsorships SET withdrawn_on = NULL WHERE withdrawn_on IS NOT NULL", "sponsorships")]
    [InlineData("DELETE FROM action_revision_sponsorship_citations WHERE position = 1", "sponsorships")]
    [InlineData("UPDATE action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{authorSubject}', '\"auth0|private\"')::text", "authorSubject")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsTextBecauseTheTextIsPrivate(string change, string field)
    {
        await SettleFullRevisionAsync();
        await ExecuteAsync(connectionString, change);

        var verification = await VerifyActionRevisionsAsync();

        Assert.Equal(1, verification.Mismatched);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await SettleFullRevisionAsync();
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET revision_json = 'x'");
        var snapshot = await ActionRevisionSnapshotAsync();

        var mismatch = Assert.Single((await VerifyActionRevisionsAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await ActionRevisionSnapshotAsync());
    }

    private Task MakeActionRevisionsLegacyAsync(params int[] numbers)
    {
        var list = string.Join(", ", numbers);
        return ExecuteAsync(connectionString, $"""
            DELETE FROM action_revision_sponsorship_citations WHERE revision_number IN ({list});
            DELETE FROM action_revision_sponsorships WHERE revision_number IN ({list});
            DELETE FROM action_revision_issues WHERE revision_number IN ({list});
            UPDATE action_revisions SET {NullRevisionColumns} WHERE revision_number IN ({list})
            """);
    }

    private async Task RewriteActionRevisionJsonAsync(int number, Action<JsonObject> change)
    {
        var json = JsonNode.Parse((string)(await ScalarAsync($"SELECT revision_json FROM action_revisions WHERE revision_number = {number}"))!)!.AsObject();
        change(json);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE action_revisions SET revision_json = @json WHERE revision_number = @number", connection);
        command.Parameters.AddWithValue("json", json.ToJsonString());
        command.Parameters.AddWithValue("number", number);
        await command.ExecuteNonQueryAsync();
    }

    private Task<RevisionBackfillResult> BackfillActionRevisionsAsync(int limit) =>
        new BackfillRevisions(ActionRevisionBackfillStore()).ExecuteAsync(BackfillOwner, limit);

    private Task<RevisionVerification> VerifyActionRevisionsAsync() => new VerifyRevisions(ActionRevisionBackfillStore()).ExecuteAsync(BackfillOwner);

    private PostgresActionRevisionBackfillStore ActionRevisionBackfillStore() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task<List<string>> ActionRevisionSnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "action_revisions", "action_revision_issues", "action_revision_sponsorships", "action_revision_sponsorship_citations" })
            snapshot.AddRange(await LinesAsync($"SELECT to_jsonb(t)::text FROM {table} t ORDER BY 1"));
        return snapshot;
    }
}
