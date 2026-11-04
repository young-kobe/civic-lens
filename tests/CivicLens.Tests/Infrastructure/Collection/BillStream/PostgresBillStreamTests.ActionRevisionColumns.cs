using System.Text.Json.Nodes;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly string AbsentId = new('9', 64);

    [Fact]
    public async Task AFullRevisionWritesColumnsAndChildRowsThatEqualItsJsonBecauseTheReadersWillMoveToThem()
    {
        var full = await SettleFullRevisionAsync();

        await AssertColumnsEqualAsync(full);
    }

    [Fact]
    public async Task ALegacyShapeRevisionWritesTrackedIssueRowsOnlyAndRecordsThatNoCandidatesExistBecauseItsCandidatesAreUnknown()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var legacy = await SettleAsync(candidate, Revision(candidate, 1) with { IssueCandidateIds = default, TrackedIssueIds = ["issue-a", "issue-b"] });

        await AssertColumnsEqualAsync(legacy);
        Assert.Equal(["issue-a|true", "issue-b|true"], await LinesAsync($"SELECT issue_id || '|' || is_tracked FROM action_revision_issues WHERE record_id = '{RecordId}' ORDER BY position"));
        Assert.Equal(false, await ScalarAsync("SELECT issue_candidates_recorded FROM action_revisions"));
    }

    [Fact]
    public async Task ARevisionWithASuggestionWritesItsFourSuggestionColumnsBecauseAPersonRevisionKeepsTheModelTextThere()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        var firstRunId = (string)(await ScalarAsync("SELECT run_id FROM action_revisions WHERE revision_number = 1"))!;
        var edited = await SaveAsync(1, SuggestionChoice.Keep, "save-suggestion");
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{candidate}'");

        var suggesting = await SettleAsync(candidate, Revision(candidate, 3) with
        {
            Headline = edited.Headline,
            Summary = edited.Summary,
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = new("Model headline", "Model summary.", firstRunId, new string('7', 64))
        });

        await AssertColumnsEqualAsync(suggesting);
    }

    [Fact]
    public async Task ARevisionThatNamesAnExtractionThatIsNotStoredFailsTheSettlementOnTheForeignKeyAndWritesNothing()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => store.CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(candidate, 1)),
            DraftedRun(runId, candidate), default));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
        Assert.Equal("Running", await ScalarAsync($"SELECT status FROM action_analysis WHERE candidate_id = '{candidate}'"));
    }

    [Fact]
    public async Task APersonEditWritesItsColumnsTooBecauseThePersonPathBuildsTheRowWithTheSameMapping()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));

        var edited = await SaveAsync(1, SuggestionChoice.Keep, "save-columns");

        await AssertColumnsEqualAsync(edited);
        Assert.Equal("Person", await ScalarAsync($"SELECT text_origin FROM action_revisions WHERE record_id = '{RecordId}' AND revision_number = 2"));
    }

    [Fact]
    public async Task ARowThatOlderCodeWritesHasNoNewColumnsAndPassesEveryCheckBecauseRollbackMustStaySafe()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));

        await InsertRevisionAsync("Person", "auth0|editor", null, 2);

        Assert.Equal(0, await ScalarAsync("SELECT num_nonnulls(headline, summary, text_origin, source_id, bill_number, candidate_id, fingerprint, bill_title, issue_candidates_recorded) FROM action_revisions WHERE revision_number = 2"));
    }

    [Theory]
    [InlineData("action_revisions", "headline = ' '", "ck_action_revisions_headline")]
    [InlineData("action_revisions", "summary = ''", "ck_action_revisions_summary")]
    [InlineData("action_revisions", "text_origin = 'Robot'", "ck_action_revisions_text_origin")]
    [InlineData("action_revisions", "suggestion_headline = 'Half set'", "ck_action_revisions_suggestion")]
    [InlineData("action_revisions", "author_kind = 'Person', run_id = NULL", "ck_action_revisions_person_text")]
    [InlineData("action_revisions", "congress = 0", "ck_action_revisions_bill")]
    [InlineData("action_revisions", "bill_number = 0", "ck_action_revisions_bill")]
    [InlineData("action_revisions", "candidate_id = 'xyz'", "ck_action_revisions_candidate")]
    [InlineData("action_revisions", "fingerprint = 'xyz'", "ck_action_revisions_fingerprint")]
    [InlineData("action_revisions", "issue_revision_id = 'xyz'", "ck_action_revisions_issue_revision")]
    [InlineData("action_revisions", "subjects_hash = 'xyz'", "ck_action_revisions_subjects_hash")]
    [InlineData("action_revisions", "candidate_update_utc_ticks = -1", "ck_action_revisions_candidate_update_utc_ticks")]
    [InlineData("action_revisions", "summary_version = NULL", "ck_action_revisions_summary_version")]
    [InlineData("action_revisions", "policy_area_citation_extraction_id = NULL, policy_area_citation_start = NULL, policy_area_citation_length = NULL", "ck_action_revisions_policy_area")]
    [InlineData("action_revisions", "policy_area = NULL, policy_area_citation_extraction_id = NULL, policy_area_citation_start = NULL, policy_area_citation_length = NULL", "ck_action_revisions_policy_area_id")]
    [InlineData("action_revisions", "summary_citation_start = -1", "ck_action_revisions_summary_citation")]
    [InlineData("action_revisions", "policy_area_citation_length = 0", "ck_action_revisions_policy_area_citation")]
    [InlineData("action_revisions", "bill_citation_start = NULL", "ck_action_revisions_bill_citation")]
    [InlineData("action_revisions", "author_kind = 'Code', bill_citation_extraction_id = NULL, bill_citation_start = NULL, bill_citation_length = NULL", "ck_action_revisions_code_bill_citation")]
    [InlineData("action_revisions", "headline = NULL", "ck_action_revisions_columns_written")]
    [InlineData("action_revisions", "event_on = NULL", "ck_action_revisions_derived_columns_written")]
    [InlineData("action_revisions", "cosponsor_count = NULL", "ck_action_revisions_derived_columns_written")]
    [InlineData("action_revisions", "cosponsor_count = -1", "ck_action_revisions_cosponsor_count")]
    [InlineData("action_revision_issues", "issue_id = ' '", "ck_action_revision_issues_issue")]
    [InlineData("action_revision_issues", "position = -1", "ck_action_revision_issues_position")]
    [InlineData("action_revision_sponsorships", "role = 'Boss'", "ck_action_revision_sponsorships_role")]
    [InlineData("action_revision_sponsorships", "withdrawn_on = '2024-01-01'", "ck_action_revision_sponsorships_withdrawn")]
    [InlineData("action_revision_sponsorships", "bioguide_id = ' '", "ck_action_revision_sponsorships_person")]
    [InlineData("action_revision_sponsorships", "position = -1", "ck_action_revision_sponsorships_position")]
    [InlineData("action_revision_sponsorship_citations", "position = 2", "ck_action_revision_sponsorship_citations_position")]
    [InlineData("action_revision_sponsorship_citations", "citation_start = -1", "ck_action_revision_sponsorship_citations_citation")]
    public async Task TheDatabaseRefusesAColumnValueThatBreaksARuleOfTheRevision(string table, string assignment, string constraint)
    {
        _ = await SettleFullRevisionAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"UPDATE {table} SET {assignment}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(constraint, failure.ConstraintName);
    }

    [Theory]
    [InlineData("action_revisions", "candidate_id = repeat('9', 64)")]
    [InlineData("action_revisions", "bill_number = 99")]
    [InlineData("action_revisions", "bill_citation_extraction_id = repeat('9', 64)")]
    [InlineData("action_revisions", "suggestion_headline = 'H', suggestion_summary = 'S', suggestion_run_id = 'norun', suggestion_fingerprint = fingerprint")]
    [InlineData("action_revision_sponsorship_citations", "citation_extraction_id = repeat('9', 64)")]
    public async Task TheDatabaseRefusesAReferenceToARowThatDoesNotExistBecauseARevisionNamesItsCandidateBillRunAndCitedText(string table, string assignment)
    {
        _ = await SettleFullRevisionAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"UPDATE {table} SET {assignment}"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task ARevisionOfOnePersonMayRepeatTheSameRoleBecauseTheSponsorshipKeyIsThePositionNotThePerson()
    {
        var full = await SettleFullRevisionAsync();

        Assert.Equal(2L, await ScalarAsync($"SELECT count(*) FROM action_revision_sponsorships WHERE record_id = '{RecordId}' AND person_id = '{full.Sponsorships[0].PersonId}' AND role = 'Sponsor'"));
    }

    [Fact]
    public async Task TheEventDateAndTheCosponsorCountAreWrittenOnceWithTheRevisionBecauseARevisionNeverChangesAndTheApiSortsByThem()
    {
        var full = await SettleFullRevisionAsync();

        Assert.Equal(("2025-02-01", 1), (full.EventOn().ToString("yyyy-MM-dd"), full.CosponsorCount()));
        Assert.Equal("2025-02-01|1", await ScalarAsync("SELECT event_on || '|' || cosponsor_count FROM action_revisions"));
        Assert.Equal("ix_action_revisions_event_on", await ScalarAsync("SELECT indexname FROM pg_indexes WHERE tablename = 'action_revisions' AND indexdef LIKE '%(event_on)%'"));
    }

    private async Task<ActionDraftRevision> SettleFullRevisionAsync()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        var basis = Revision(candidate, 1, includesSummaries: true, includesSubjects: true);
        var extractionId = basis.Sponsorships[0].Citations[0].ExtractionId;
        var span = new SourceSpan(extractionId, 2, 3);
        var summarySpan = new SourceSpan(extractionId, 1, 4);
        var policyAreaSpan = new SourceSpan(extractionId, 6, 7);
        var billSpan = new SourceSpan(extractionId, 3, 9);
        var full = basis with
        {
            SummaryVersion = "summary-v1",
            SummaryCitation = summarySpan,
            PolicyArea = "Housing",
            PolicyAreaCitation = policyAreaSpan,
            PolicyAreaId = "housing",
            BillCitation = billSpan,
            IssueCandidateIds = ["issue-a", "issue-b", "issue-c"],
            TrackedIssueIds = ["issue-b"],
            SubjectsHash = new string('5', 64),
            Sponsorships =
            [
                basis.Sponsorships[0],
                basis.Sponsorships[0] with { IsOriginal = true, WithdrawnOn = new DateOnly(2025, 2, 1), Citations = [span, new(extractionId, 6, 4)] },
                new("cosponsor", "C000003", ActionSponsorshipRole.Cosponsor, false, new DateOnly(2025, 1, 9), null, [span])
            ]
        };
        return await SettleAsync(candidate, full);
    }

    private async Task AssertColumnsEqualAsync(ActionDraftRevision revision)
    {
        var key = $"record_id = '{revision.RecordId}' AND revision_number = {revision.RevisionNumber}";
        var actual = JsonNode.Parse((string)(await ScalarAsync($"SELECT to_jsonb(t)::text FROM action_revisions t WHERE {key}"))!)!.AsObject();
        var expected = new JsonObject
        {
            ["headline"] = revision.Headline,
            ["summary"] = revision.Summary,
            ["text_origin"] = revision.TextOrigin.ToString(),
            ["suggestion_headline"] = revision.Suggestion?.Headline,
            ["suggestion_summary"] = revision.Suggestion?.Summary,
            ["suggestion_run_id"] = revision.Suggestion?.RunId,
            ["suggestion_fingerprint"] = revision.Suggestion?.Fingerprint,
            ["source_id"] = revision.SourceId,
            ["congress"] = revision.Congress,
            ["bill_type"] = revision.BillType,
            ["bill_number"] = revision.BillNumber,
            ["candidate_id"] = revision.CandidateId,
            ["candidate_update_utc_ticks"] = revision.CandidateUpdateDate.UtcTicks,
            ["candidate_includes_summaries"] = revision.CandidateIncludesSummaries,
            ["candidate_includes_subjects"] = revision.CandidateIncludesSubjects,
            ["fingerprint"] = revision.Fingerprint,
            ["summary_version"] = revision.SummaryVersion,
            ["summary_citation_extraction_id"] = revision.SummaryCitation?.ExtractionId,
            ["summary_citation_start"] = revision.SummaryCitation?.Start,
            ["summary_citation_length"] = revision.SummaryCitation?.Length,
            ["policy_area"] = revision.PolicyArea,
            ["policy_area_citation_extraction_id"] = revision.PolicyAreaCitation?.ExtractionId,
            ["policy_area_citation_start"] = revision.PolicyAreaCitation?.Start,
            ["policy_area_citation_length"] = revision.PolicyAreaCitation?.Length,
            ["policy_area_id"] = revision.PolicyAreaId,
            ["bill_title"] = revision.BillTitle,
            ["bill_citation_extraction_id"] = revision.BillCitation?.ExtractionId,
            ["bill_citation_start"] = revision.BillCitation?.Start,
            ["bill_citation_length"] = revision.BillCitation?.Length,
            ["issue_revision_id"] = revision.IssueRevisionId,
            ["subjects_hash"] = revision.SubjectsHash,
            ["issue_candidates_recorded"] = !revision.IssueCandidateIds.IsDefault,
            ["event_on"] = revision.EventOn().ToString("yyyy-MM-dd"),
            ["cosponsor_count"] = revision.CosponsorCount()
        };
        foreach (var (column, value) in expected)
            Assert.True((value?.ToJsonString() ?? "null") == (actual[column]?.ToJsonString() ?? "null"), $"Column {column} differs from the JSON.");

        var issueIds = revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;
        Assert.Equal(issueIds.Select((id, position) => $"{position}|{id}|{revision.TrackedIssueIds.Contains(id)}".ToLowerInvariant()),
            await LinesAsync($"SELECT position || '|' || issue_id || '|' || is_tracked FROM action_revision_issues WHERE {key} ORDER BY position"));
        Assert.Equal(revision.Sponsorships.Select((item, position) =>
                $"{position}|{item.PersonId}|{item.BioguideId}|{item.Role}|{item.IsOriginal.ToString().ToLowerInvariant()}|{item.SponsoredOn:yyyy-MM-dd}|{item.WithdrawnOn:yyyy-MM-dd}"),
            await LinesAsync($"SELECT position || '|' || person_id || '|' || bioguide_id || '|' || role || '|' || is_original || '|' || sponsored_on || '|' || COALESCE(withdrawn_on::text, '') FROM action_revision_sponsorships WHERE {key} ORDER BY position"));
        Assert.Equal(revision.Sponsorships.SelectMany((item, position) => item.Citations.Select((citation, citationPosition) =>
                $"{position}|{citationPosition}|{citation.ExtractionId}|{citation.Start}|{citation.Length}")),
            await LinesAsync($"SELECT sponsorship_position || '|' || position || '|' || citation_extraction_id || '|' || citation_start || '|' || citation_length FROM action_revision_sponsorship_citations WHERE {key} ORDER BY sponsorship_position, position"));
    }

    private async Task<List<string>> LinesAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return lines;
    }
}
