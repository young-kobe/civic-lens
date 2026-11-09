using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    [Fact]
    public async Task AFullRevisionWritesColumnsAndChildRowsThatEqualTheRevisionBecauseTheReadersUseThem()
    {
        var candidate = await PendingCandidateAsync();
        var basis = await RevisionAsync(candidate, 1);
        var span = new SourceSpan(basis.Facts[0].Citation.ExtractionId, 2, 3);
        var memberSpan = new SourceSpan(basis.Members[0].Citation.ExtractionId, 1, 4);
        var full = basis with
        {
            Kind = VoteKind.Passage,
            KindCitation = span,
            Facts = [basis.Facts[0], new("Question: On Passage", span), new("Second fact", span)],
            Members = [basis.Members[0], new("with-id-b", "B000002", "Nay", memberSpan), new("with-id-c", "C000003", "Not Voting", span)],
            IssueCandidateIds = ["issue-a", "issue-b", "issue-c"],
            TrackedIssueIds = ["issue-b"],
            SubjectsHash = new string('5', 64)
        };

        await SettleAsync(candidate, full);

        await AssertColumnsEqualAsync(full);
    }

    [Fact]
    public async Task ARevisionWithoutAResultLineIsRefusedAndWritesNoRowBecauseTheResultColumnIsRequired()
    {
        var candidate = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        var withoutResult = (await RevisionAsync(candidate, 1)) with { Facts = [new("Question: On Passage", (await RevisionAsync(candidate, 1)).Facts[0].Citation)] };

        await Assert.ThrowsAsync<ArgumentException>(() => store.CheckpointAsync(VoteDraftCheckpoint.From(claim, withoutResult), DraftedRun(runId, candidate), default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_revisions"));
    }

    [Fact]
    public async Task ARevisionWithoutIssueCandidatesWritesTrackedIssueRowsOnlyAndRecordsThatNoCandidatesExistBecauseItsCandidatesAreUnknown()
    {
        var candidate = await PendingCandidateAsync();
        var withoutCandidates = (await RevisionAsync(candidate, 1)) with { IssueCandidateIds = default, TrackedIssueIds = ["issue-a", "issue-b"], SubjectsHash = null };

        await SettleAsync(candidate, withoutCandidates);

        await AssertColumnsEqualAsync(withoutCandidates);
        Assert.Equal(["issue-a|true", "issue-b|true"], await LinesAsync($"SELECT issue_id || '|' || is_tracked FROM vote_revision_issues WHERE record_id = '{withoutCandidates.RecordId}' ORDER BY position"));
        Assert.Equal(false, await ScalarAsync("SELECT issue_candidates_recorded FROM vote_revisions"));
    }

    [Fact]
    public async Task EveryRevisionOfARecordWithPersonTextAndASuggestionWritesItsColumnsBecauseThePersonPathWritesThemToo()
    {
        var recordId = await RecordWithPersonTextAndSuggestionAsync();

        foreach (var number in new[] { 1, 2, 3 })
            await AssertColumnsEqualAsync((await VoteReviews().GetRevisionAsync(recordId, number, default))!);

        Assert.NotNull((await VoteReviews().GetRevisionAsync(recordId, 3, default))!.Suggestion);
    }

    [Fact]
    public async Task TwoRevisionsWithTheSameMembersShareOneListRowBecauseAListNeverChanges()
    {
        var older = await PendingCandidateAsync();
        var first = await RevisionAsync(older, 1);
        await SettleAsync(older, first);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));

        await SettleAsync(newer, (await RevisionAsync(newer, 2)) with { Members = first.Members });

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_member_lists"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_member_list_entries"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(DISTINCT member_list_id) FROM vote_revisions"));
        Assert.Equal("1|0|0|0|0", await ScalarAsync("SELECT yea_count || '|' || nay_count || '|' || present_count || '|' || not_voting_count || '|' || other_count FROM vote_member_lists"));
    }

    [Fact]
    public async Task AStoredMemberListCountsAliasesAndOtherCastsWithoutChangingRawCasts()
    {
        var candidate = await PendingCandidateAsync();
        var basis = await RevisionAsync(candidate, 1);
        var casts = new[] { "Yea", "Aye", "Nay", "No", "Present", "Not Voting", "Excused", "Jeffries" };
        var members = casts.Select((cast, index) => basis.Members[0] with
        {
            PersonId = $"person-{index}",
            BioguideId = $"X{index:000000}",
            Cast = cast
        }).ToImmutableArray();
        var revision = basis with { Members = members };

        await SettleAsync(candidate, revision);

        Assert.Equal((short)8, await ScalarAsync("SELECT member_count FROM vote_member_lists"));
        Assert.Equal("2|2|1|1|2", await ScalarAsync("SELECT yea_count || '|' || nay_count || '|' || present_count || '|' || not_voting_count || '|' || other_count FROM vote_member_lists"));
        Assert.Equal(casts, await LinesAsync("SELECT vote_cast FROM vote_member_list_entries ORDER BY position"));
    }

    [Fact]
    public async Task AChangedCastMakesANewListAndLeavesTheFirstListUntouchedBecauseTheOlderRevisionKeepsItsVotes()
    {
        var older = await PendingCandidateAsync();
        var first = await RevisionAsync(older, 1);
        await SettleAsync(older, first);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var second = await RevisionAsync(newer, 2);

        await SettleAsync(newer, second with { Members = [first.Members[0] with { Cast = "Nay" }] });

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_member_lists"));
        Assert.Equal(["Yea", "Nay"], await LinesAsync("SELECT e.vote_cast FROM vote_revisions r JOIN vote_member_list_entries e ON e.list_id = r.member_list_id ORDER BY r.revision_number"));
        await AssertColumnsEqualAsync(first);
    }

    // The list ID hashes the citation too, so a member list never silently points to a different extraction.
    [Fact]
    public async Task EqualCastsWithANewExtractionMakeANewListBecauseTheCitationIsPartOfTheListIdentity()
    {
        var older = await PendingCandidateAsync();
        var first = await RevisionAsync(older, 1);
        await SettleAsync(older, first);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var second = await RevisionAsync(newer, 2);
        var recited = first.Members[0] with { Citation = second.Members[0].Citation };
        Assert.NotEqual(first.Members[0].Citation.ExtractionId, recited.Citation.ExtractionId);

        await SettleAsync(newer, second with { Members = [recited] });

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_member_lists"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(DISTINCT member_list_id) FROM vote_revisions"));
    }

    [Fact]
    public async Task ANewerCandidateMayChangeTheBillSoTheBillStaysOffTheRollIdentityKey()
    {
        var older = await PendingCandidateAsync();
        await SettleAsync(older, await RevisionAsync(older, 1));
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var second = await RevisionAsync(newer, 2);

        await SettleAsync(newer, second with { BillType = "hr", BillNumber = (second.BillNumber ?? 0) + 1 });

        Assert.Equal(2L, await ScalarAsync("SELECT count(DISTINCT bill_number) FROM vote_revisions"));
    }

    [Theory]
    [InlineData("headline")]
    [InlineData("text_origin")]
    [InlineData("chamber")]
    [InlineData("member_list_id")]
    [InlineData("kind")]
    [InlineData("voted_on_citation_length")]
    [InlineData("issue_candidates_recorded")]
    public async Task TheDatabaseRefusesANullInAColumnThatEveryRevisionSetsBecauseNoReaderGuessesAValue(string column)
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE vote_revisions SET {column} = NULL"));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
        Assert.Equal(column, failure.ColumnName);
    }

    [Theory]
    [InlineData("vote_revisions", "headline = ' '", "ck_vote_revisions_headline")]
    [InlineData("vote_revisions", "summary = ''", "ck_vote_revisions_summary")]
    [InlineData("vote_revisions", "text_origin = 'Robot'", "ck_vote_revisions_text_origin")]
    [InlineData("vote_revisions", "suggestion_headline = 'Half set'", "ck_vote_revisions_suggestion")]
    [InlineData("vote_revisions", "chamber = 'Senate'", "ck_vote_revisions_chamber_source")]
    [InlineData("vote_revisions", "congress = 0", "ck_vote_revisions_roll")]
    [InlineData("vote_revisions", "bill_number = 0", "ck_vote_revisions_bill")]
    [InlineData("vote_revisions", "kind = 'Bogus'", "ck_vote_revisions_kind")]
    [InlineData("vote_revisions", "kind_citation_start = 1", "ck_vote_revisions_kind_citation")]
    [InlineData("vote_revisions", "voted_on_citation_length = 0", "ck_vote_revisions_voted_on_citation")]
    [InlineData("vote_revisions", "issue_revision_id = 'xyz'", "ck_vote_revisions_issue_revision")]
    [InlineData("vote_revisions", "subjects_hash = 'xyz'", "ck_vote_revisions_subjects_hash")]
    [InlineData("vote_revisions", "candidate_update_utc_ticks = -1", "ck_vote_revisions_candidate_update_utc_ticks")]
    [InlineData("vote_revisions", "chamber = 'Other'", "ck_vote_revisions_chamber")]
    [InlineData("vote_revisions", "chamber = 'Senate', vote_source = 'Bogus'", "ck_vote_revisions_vote_source")]
    [InlineData("vote_member_lists", "member_count = 601, yea_count = 601, nay_count = 0, present_count = 0, not_voting_count = 0, other_count = 0", "ck_vote_member_lists_count")]
    [InlineData("vote_member_lists", "other_count = -1", "ck_vote_member_lists_totals")]
    [InlineData("vote_member_lists", "other_count = other_count + 1", "ck_vote_member_lists_totals")]
    [InlineData("vote_member_lists", "list_id = 'xyz'", "ck_vote_member_lists_id")]
    [InlineData("vote_member_lists", "created_at_utc_ticks = -1", "ck_vote_member_lists_created_at_utc_ticks")]
    [InlineData("vote_member_list_entries", "position = -1", "ck_vote_member_list_entries_position")]
    [InlineData("vote_member_list_entries", "vote_cast = ' '", "ck_vote_member_list_entries_member")]
    [InlineData("vote_member_list_entries", "citation_length = 0", "ck_vote_member_list_entries_citation")]
    [InlineData("vote_revision_facts", "fact_text = ''", "ck_vote_revision_facts_text")]
    [InlineData("vote_revision_facts", "citation_start = -1", "ck_vote_revision_facts_citation")]
    [InlineData("vote_revision_issues", "position = -1", "ck_vote_revision_issues_position")]
    [InlineData("vote_revision_issues", "issue_id = ' '", "ck_vote_revision_issues_issue")]
    public async Task TheDatabaseRefusesAColumnValueThatBreaksARuleOfTheRevision(string table, string assignment, string constraint)
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, (await RevisionAsync(candidate, 1)) with { IssueCandidateIds = ["issue-a"], TrackedIssueIds = ["issue-a"] });

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE {table} SET {assignment}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal(constraint, failure.ConstraintName);
    }

    [Fact]
    public async Task ARevisionCannotNameAnotherRollCallThanItsRecordBecauseTheRollIdentityIsAForeignKey()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE vote_revisions SET roll_call_number = 101"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    // The record ID is the key, so the chamber and the roll alone fix the identity. The source ID is a label and never part of the key.
    [Fact]
    public async Task ARevisionWithAnotherSourceIdKeepsTheRollIdentityOfItsRecordBecauseTheSourceIsNotPartOfTheKey()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        await ExecuteAsync("UPDATE vote_revisions SET source_id = 'another-source'");

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_revisions r JOIN vote_drafts d USING (record_id, chamber, congress, session_number, roll_call_number) WHERE r.source_id <> d.source_id"));
    }

    private async Task AssertColumnsEqualAsync(VoteDraftRevision revision)
    {
        var key = $"record_id = '{revision.RecordId}' AND revision_number = {revision.RevisionNumber}";
        var actual = JsonNode.Parse((string)(await ScalarAsync($"SELECT to_jsonb(t)::text FROM vote_revisions t WHERE {key}"))!)!.AsObject();
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
            ["vote_source"] = revision.Source.ToString(),
            ["chamber"] = revision.Chamber.ToString(),
            ["congress"] = revision.Congress,
            ["session_number"] = revision.SessionNumber,
            ["roll_call_number"] = revision.RollCallNumber,
            ["bill_type"] = revision.BillType,
            ["bill_number"] = revision.BillNumber,
            ["candidate_id"] = revision.CandidateId,
            ["candidate_update_utc_ticks"] = revision.CandidateUpdateDate.UtcTicks,
            ["voted_on"] = revision.VotedOn.ToString("yyyy-MM-dd"),
            ["voted_on_citation_extraction_id"] = revision.VotedOnCitation.ExtractionId,
            ["voted_on_citation_start"] = revision.VotedOnCitation.Start,
            ["voted_on_citation_length"] = revision.VotedOnCitation.Length,
            ["kind"] = revision.Kind.ToString(),
            ["kind_citation_extraction_id"] = revision.KindCitation?.ExtractionId,
            ["kind_citation_start"] = revision.KindCitation?.Start,
            ["kind_citation_length"] = revision.KindCitation?.Length,
            ["issue_revision_id"] = revision.IssueRevisionId,
            ["subjects_hash"] = revision.SubjectsHash,
            ["issue_candidates_recorded"] = !revision.IssueCandidateIds.IsDefault,
            ["question"] = revision.Facts.FirstOrDefault(fact => fact.Text.StartsWith("Question: ", StringComparison.Ordinal))?.Text["Question: ".Length..],
            ["result"] = revision.Facts.First(fact => fact.Text.StartsWith("Result: ", StringComparison.Ordinal)).Text["Result: ".Length..]
        };
        foreach (var (column, value) in expected)
            Assert.True((value?.ToJsonString() ?? "null") == (actual[column]?.ToJsonString() ?? "null"), $"Column {column} differs from the revision.");

        Assert.Equal(revision.Facts.Select((fact, position) => Line(position, fact.Text, fact.Citation)),
            await LinesAsync($"SELECT position || '|' || fact_text || '|' || citation_extraction_id || '|' || citation_start || '|' || citation_length FROM vote_revision_facts WHERE {key} ORDER BY position"));
        var issueIds = revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;
        Assert.Equal(issueIds.Select((id, position) => $"{position}|{id}|{revision.TrackedIssueIds.Contains(id)}".ToLowerInvariant()),
            await LinesAsync($"SELECT position || '|' || issue_id || '|' || is_tracked FROM vote_revision_issues WHERE {key} ORDER BY position"));
        Assert.Equal(revision.Members.Select((member, position) => Line(position, member.PersonId + "|" + member.BioguideId + "|" + member.Cast, member.Citation)),
            await LinesAsync($"""
                SELECT e.position || '|' || e.person_id || '|' || e.bioguide_id || '|' || e.vote_cast || '|' || e.citation_extraction_id || '|' || e.citation_start || '|' || e.citation_length
                FROM vote_member_list_entries e JOIN vote_revisions r ON r.member_list_id = e.list_id WHERE r.{key} ORDER BY e.position
                """));
        Assert.Equal((short)revision.Members.Length,
            await ScalarAsync($"SELECT l.member_count FROM vote_member_lists l JOIN vote_revisions r ON r.member_list_id = l.list_id WHERE r.{key}"));
    }

    private static string Line(int position, string text, SourceSpan citation) =>
        $"{position}|{text}|{citation.ExtractionId}|{citation.Start}|{citation.Length}";

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
