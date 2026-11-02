using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private const string NullColumns = """
        headline = NULL, summary = NULL, text_origin = NULL, suggestion_headline = NULL, suggestion_summary = NULL, suggestion_run_id = NULL,
        suggestion_fingerprint = NULL, source_id = NULL, vote_source = NULL, chamber = NULL, congress = NULL, session_number = NULL,
        roll_call_number = NULL, bill_type = NULL, bill_number = NULL, candidate_id = NULL, candidate_update_utc_ticks = NULL, voted_on = NULL,
        voted_on_citation_extraction_id = NULL, voted_on_citation_start = NULL, voted_on_citation_length = NULL, kind = NULL,
        kind_citation_extraction_id = NULL, kind_citation_start = NULL, kind_citation_length = NULL, member_list_id = NULL,
        issue_revision_id = NULL, subjects_hash = NULL, issue_candidates_recorded = NULL
        """;

    private static readonly ReviewActor BackfillOwner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouchedBecauseReadersStillUseTheJson()
    {
        var full = await SettledLegacyAsync(basis => basis with
        {
            Kind = VoteKind.Passage,
            KindCitation = new(basis.Facts[0].Citation.ExtractionId, 2, 3),
            Facts = [basis.Facts[0], new("Second fact", basis.Facts[0].Citation)],
            Members = [basis.Members[0], new("with-id-b", "B000002", "Nay", basis.Members[0].Citation)],
            IssueCandidateIds = ["issue-c", "issue-a", "issue-b"],
            TrackedIssueIds = ["issue-a", "issue-c"],
            SubjectsHash = new string('5', 64)
        });
        var jsonBefore = await ScalarAsync("SELECT revision_json FROM vote_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        await AssertColumnsEqualAsync(full);
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT revision_json FROM vote_revisions"));
        var verification = await VerifyAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task ARevisionWithNoCandidateSetKeepsItsTrackedIssuesInOrderAndRecordsThatCandidatesAreUnknown()
    {
        await SettledLegacyAsync(basis => basis with { IssueCandidateIds = default, TrackedIssueIds = ["issue-b", "issue-a"], SubjectsHash = null });

        var result = await BackfillAsync(10);

        Assert.Equal(1, result.Moved);
        Assert.Equal(["issue-b|true", "issue-a|true"], await LinesAsync("SELECT issue_id || '|' || is_tracked FROM vote_revision_issues ORDER BY position"));
        Assert.Equal(0, (await VerifyAsync()).Mismatched);
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns()
    {
        await SettledLegacyAsync(basis => basis);
        await BackfillAsync(10);
        var snapshot = await TableSnapshotAsync();

        var rerun = await BackfillAsync(10);

        Assert.Equal((0, 0, 0), (rerun.Examined, rerun.Moved, rerun.Skipped));
        Assert.Equal(snapshot, await TableSnapshotAsync());
    }

    [Fact]
    public async Task TheLimitBoundsTheRevisionsOfOneRunAndTheNextRunResumesWhereTheLastStoppedBecauseARunMayStopAnywhere()
    {
        var first = await SettledLegacyAsync(basis => basis);
        var second = await AddRevisionAsync(first, 2, legacy: true);
        await AddRevisionAsync(second, 3, legacy: true);

        var one = await BackfillAsync(2);
        var two = await BackfillAsync(2);

        Assert.Equal((2, 2), (one.Examined, one.Moved));
        Assert.Equal((1, 1), (two.Examined, two.Moved));
        Assert.Equal(3, (await VerifyAsync()).Checked);
    }

    [Fact]
    public async Task RevisionsWithTheSameMembersShareOneListAfterTheBackfillBecauseAListNeverChanges()
    {
        var first = await SettledLegacyAsync(basis => basis);
        await AddRevisionAsync(first, 2, legacy: true);

        await BackfillAsync(10);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_member_lists"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(DISTINCT member_list_id) FROM vote_revisions"));
    }

    [Fact]
    public async Task ARowThatCannotMoveIsSkippedAndTheRowsAroundItStillMoveBecauseOneBadRowMustNotStopTheRun()
    {
        var first = await SettledLegacyAsync(basis => basis);
        var second = await AddRevisionAsync(first, 2, legacy: true);
        await AddRevisionAsync(second, 3, legacy: true);
        await RewriteJsonAsync(2, json => json["members"] = new JsonArray());

        var result = await BackfillAsync(10);

        Assert.Equal((3, 2, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(new SkippedVoteRevision(first.RecordId, 2, "InvalidShape"), Assert.Single(result.SkippedRevisions));
        Assert.Equal([2], (await LinesAsync("SELECT revision_number::text FROM vote_revisions WHERE headline IS NULL")).Select(int.Parse));
    }

    [Fact]
    public async Task AnOldRevisionThatFailsTheShapeRulesOfTodayIsReportedAndNeverMovedOrChanged()
    {
        await SettledLegacyAsync(basis => basis);
        await RewriteJsonAsync(1, json => json["facts"] = new JsonArray(Enumerable.Range(0, VoteDraftRevision.MaximumFacts + 1)
            .Select(number => (JsonNode)new JsonObject { ["text"] = $"Fact {number}", ["citation"] = json["votedOnCitation"]!.DeepClone() }).ToArray()));
        var jsonBefore = await ScalarAsync("SELECT revision_json FROM vote_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT revision_json FROM vote_revisions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_revisions WHERE headline IS NULL"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_revision_facts"));
    }

    [Fact]
    public async Task DuplicateIssueCandidateIdsAreSkippedBecauseTheIssueKeyHoldsEachIdOnceAndTheRowsCouldNotRebuildTheList()
    {
        await SettledLegacyAsync(basis => basis);
        await RewriteJsonAsync(1, json =>
        {
            json["issueCandidateIds"] = new JsonArray("issue-a", "issue-b", "issue-a");
            json["trackedIssueIds"] = new JsonArray("issue-a");
        });

        var result = await BackfillAsync(10);

        Assert.Equal("DuplicateIssueId", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_revision_issues"));
    }

    [Fact]
    public async Task TrackedIssuesOutOfOrderAreSkippedBecauseTheRowsReadBackInOrdinalOrderAndWouldChangeTheRevision()
    {
        await SettledLegacyAsync(basis => basis);
        await RewriteJsonAsync(1, json =>
        {
            json["issueCandidateIds"] = new JsonArray("issue-a", "issue-b");
            json["trackedIssueIds"] = new JsonArray("issue-b", "issue-a");
        });

        var result = await BackfillAsync(10);

        Assert.Equal("TrackedIssueOrder", Assert.Single(result.SkippedRevisions).Reason);
    }

    [Theory]
    [InlineData("candidateId", "vote_candidates_candidate_id")]
    [InlineData("votedOnCitation", "revisions_document_extractions_voted_on_citation")]
    [InlineData("facts", "revision_facts_document_extractions")]
    [InlineData("members", "member_list_entries_document_extractions")]
    [InlineData("suggestionRun", "vote_analysis_runs_suggestion_run_id")]
    [InlineData("roll", "vote_drafts_record_id_chamber_congress")]
    public async Task AnIdThatDanglesIsSkippedAndNothingIsGuessedBecauseAForeignKeyWouldRefuseIt(string dangling, string constraintPart)
    {
        await SettledLegacyAsync(basis => basis);
        var listsBefore = await ScalarAsync("SELECT count(*) FROM vote_member_lists");
        var missing = new string('d', 64);
        await RewriteJsonAsync(1, json =>
        {
            var citation = new JsonObject { ["extractionId"] = missing, ["start"] = 0, ["length"] = 1 };
            switch (dangling)
            {
                case "candidateId": json["candidateId"] = missing; break;
                case "votedOnCitation": json["votedOnCitation"] = citation; break;
                case "facts": json["facts"]![0]!["citation"] = citation; break;
                case "members": json["members"]![0]!["citation"] = citation; break;
                case "suggestionRun":
                    json["suggestion"] = new JsonObject { ["headline"] = "H", ["summary"] = "S", ["runId"] = new string('c', 32), ["fingerprint"] = new string('f', 64) };
                    break;
                default: json["rollCallNumber"] = 999; break;
            }
        });

        var result = await BackfillAsync(10);

        var reason = Assert.Single(result.SkippedRevisions).Reason;
        Assert.StartsWith("database:FK_", reason, StringComparison.Ordinal);
        Assert.Contains(constraintPart, reason, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_revisions WHERE headline IS NULL"));
        Assert.Equal(listsBefore, await ScalarAsync("SELECT count(*) FROM vote_member_lists"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_revision_facts"));
    }

    [Fact]
    public async Task AnIdOverItsColumnLengthIsSkippedAndNeverCutBecauseCuttingAnIdWouldNameAnotherRecord()
    {
        var first = await SettledLegacyAsync(basis => basis);
        await RewriteJsonAsync(1, json => json["members"]![0]!["personId"] = new string('p', VoteDraftRevision.MaximumPersonIdLength + 1));
        var second = await AddRevisionAsync(first, 2, legacy: true);
        await RewriteJsonAsync(2, json => json["suggestion"] = new JsonObject
        {
            ["headline"] = "H",
            ["summary"] = "S",
            ["runId"] = new string('a', 33),
            ["fingerprint"] = new string('f', 64)
        });

        var result = await BackfillAsync(10);

        Assert.Equal(["InvalidShape", "InvalidShape"], result.SkippedRevisions.Select(skipped => skipped.Reason));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_revisions WHERE headline IS NULL"));
        Assert.Equal(second.RecordId, result.SkippedRevisions[1].RecordId);
    }

    [Theory]
    [InlineData("x", "UnreadableJson")]
    [InlineData("""{"a":1}""", "HeaderMismatch")]
    public async Task JsonThatIsNoRevisionIsSkippedWithAReasonBecauseTheBackfillNeverGuessesAFieldFromIt(string json, string reason)
    {
        await SettledLegacyAsync(basis => basis);
        await ExecuteAsync($"UPDATE vote_revisions SET revision_json = '{json}'");

        var result = await BackfillAsync(10);

        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
    }

    [Fact]
    public async Task ARevisionJsonThatNamesAnotherKeyIsSkippedBecauseTheRowKeyAndTheJsonMustAgree()
    {
        await SettledLegacyAsync(basis => basis);
        await RewriteJsonAsync(1, json => json["revisionNumber"] = 7);

        Assert.Equal("HeaderMismatch", Assert.Single((await BackfillAsync(10)).SkippedRevisions).Reason);
    }

    [Fact]
    public async Task TheBackfillWaitsForTheVoteReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch()
    {
        await SettledLegacyAsync(basis => basis);
        await using var holder = new NpgsqlConnection(connectionString);
        await holder.OpenAsync();
        await using var held = await holder.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-vote-review', 0))", holder, held))
            await command.ExecuteNonQueryAsync();

        var running = BackfillAsync(10);
        await Task.Delay(500);
        Assert.False(running.IsCompleted);
        await held.CommitAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task ANewRevisionWrittenDuringTheBackfillIsSafeBecauseBothTakeTheVoteReviewLockAndTheNewRowHasItsOwnColumns()
    {
        var first = await SettledLegacyAsync(basis => basis);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var second = (await RevisionAsync(newer, 2)) with { Members = first.Members };

        var backfill = BackfillAsync(10);
        await SettleAsync(newer, second);
        var result = await backfill;

        Assert.Equal((1, 1), (result.Examined, result.Moved));
        var verification = await VerifyAsync();
        Assert.Equal((2, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatchBecauseItHasNoColumnsToCompare()
    {
        await SettledLegacyAsync(basis => basis);

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE vote_revisions SET headline = 'Private changed headline'", "Headline")]
    [InlineData("UPDATE vote_revisions SET summary = 'Private changed summary.'", "Summary")]
    [InlineData("UPDATE vote_revisions SET source_id = 'other-source'", "SourceId")]
    [InlineData("UPDATE vote_revisions SET voted_on_citation_start = 4", "VotedOnCitation")]
    [InlineData("UPDATE vote_revisions SET kind = 'Passage', kind_citation_extraction_id = voted_on_citation_extraction_id, kind_citation_start = 0, kind_citation_length = 1", "Kind")]
    [InlineData("UPDATE vote_revision_facts SET fact_text = 'Private changed fact'", "Facts")]
    [InlineData("UPDATE vote_revision_issues SET is_tracked = NOT is_tracked WHERE issue_id = 'issue-a'", "TrackedIssueIds")]
    [InlineData("DELETE FROM vote_revision_issues WHERE issue_id = 'issue-b'", "IssueCandidateIds")]
    [InlineData("UPDATE vote_member_list_entries SET vote_cast = 'Nay'", "Members")]
    [InlineData("UPDATE vote_revisions SET issue_candidates_recorded = false", "IssueCandidateIds")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsTextBecauseTheTextIsPrivate(string change, string field)
    {
        await SettledLegacyAsync(basis => basis with { IssueCandidateIds = ["issue-a", "issue-b"], TrackedIssueIds = ["issue-a"] });
        await BackfillAsync(10);
        await ExecuteAsync(change);

        var verification = await VerifyAsync();

        var mismatch = Assert.Single(verification.FirstMismatches);
        Assert.Equal(1, verification.Mismatched);
        Assert.Contains(field, mismatch.Fields);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(verification), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAcceptsRowsWrittenByCurrentCodeAndWritesNothingBecauseItOnlyReads()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, (await RevisionAsync(candidate, 1)) with { IssueCandidateIds = ["issue-a"], TrackedIssueIds = ["issue-a"] });
        var snapshot = await TableSnapshotAsync();

        var verification = await VerifyAsync();

        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal(snapshot, await TableSnapshotAsync());
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonBecauseTheRowsCannotBeProvedRight()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));
        await ExecuteAsync("UPDATE vote_revisions SET revision_json = 'x'");

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["RevisionJson"], mismatch.Fields);
    }

    private async Task<VoteDraftRevision> SettledLegacyAsync(Func<VoteDraftRevision, VoteDraftRevision> shape)
    {
        var candidate = await PendingCandidateAsync();
        var revision = shape((await RevisionAsync(candidate, 1)) with { IssueCandidateIds = ["issue-a", "issue-b"], TrackedIssueIds = ["issue-a"] });
        await SettleAsync(candidate, revision);
        await MakeLegacyAsync(1);
        return revision;
    }

    private async Task<VoteDraftRevision> AddRevisionAsync(VoteDraftRevision previous, int number, bool legacy)
    {
        var newer = await PendingCandidateAsync(Updated.AddDays(number));
        var revision = (await RevisionAsync(newer, number)) with { Members = previous.Members, IssueCandidateIds = previous.IssueCandidateIds, TrackedIssueIds = previous.TrackedIssueIds };
        await SettleAsync(newer, revision);
        if (legacy) await MakeLegacyAsync(number);
        return revision;
    }

    private Task MakeLegacyAsync(int number) => ExecuteAsync($"""
        DELETE FROM vote_revision_facts WHERE revision_number = {number};
        DELETE FROM vote_revision_issues WHERE revision_number = {number};
        UPDATE vote_revisions SET {NullColumns} WHERE revision_number = {number}
        """);

    private async Task RewriteJsonAsync(int number, Action<JsonObject> change)
    {
        var json = JsonNode.Parse((string)(await ScalarAsync($"SELECT revision_json FROM vote_revisions WHERE revision_number = {number}"))!)!.AsObject();
        change(json);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE vote_revisions SET revision_json = @json WHERE revision_number = @number", connection);
        command.Parameters.AddWithValue("json", json.ToJsonString());
        command.Parameters.AddWithValue("number", number);
        await command.ExecuteNonQueryAsync();
    }

    private Task<VoteRevisionBackfillResult> BackfillAsync(int limit) => new BackfillVoteRevisions(BackfillStore()).ExecuteAsync(BackfillOwner, limit);

    private Task<VoteRevisionVerification> VerifyAsync() => new VerifyVoteRevisions(BackfillStore()).ExecuteAsync(BackfillOwner);

    private PostgresVoteRevisionBackfillStore BackfillStore() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task<List<string>> TableSnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "vote_revisions", "vote_revision_facts", "vote_revision_issues", "vote_member_lists", "vote_member_list_entries" })
            snapshot.AddRange(await LinesAsync($"SELECT to_jsonb(t)::text FROM {table} t ORDER BY 1"));
        return snapshot;
    }
}
