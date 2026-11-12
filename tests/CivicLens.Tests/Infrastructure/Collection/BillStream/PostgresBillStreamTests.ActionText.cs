using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly string LongBillText = ActionBillTexts.Body + " " + string.Concat(Enumerable.Repeat("word ", 14_000));

    [Fact]
    public async Task TheEvidenceReadsTheTextThatTheGateNamesWithTheStoredCountsAndOnlyWhenAsked()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, LongBillText);

        var with = (await ActionStore().ReadEvidenceAsync(candidate, true, default))!.BillText!;
        var without = (await ActionStore().ReadEvidenceAsync(candidate, false, default))!;

        Assert.Null(without.BillText);
        Assert.Equal(("BILLS-119hr7ih", "Introduced in House", new DateOnly(2025, 3, 1), stored.ExtractionId, ActionBillTexts.ExtractionVersion),
            (with.PackageId, with.VersionType, with.VersionDate, with.ExtractionId, with.ExtractionVersion));
        Assert.Equal((stored.Reading.Text, stored.Reading.CharsRead, stored.Reading.CharsTotal), (with.Text, with.CharsRead, with.CharsTotal));
        Assert.True(with.CharsTotal > with.CharsRead);
        Assert.StartsWith("Section 1.", with.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABillWithACongressGovSummaryGetsNoTextEvenWhenAskedBecauseItsGateHoldsNone()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: true);

        var evidence = (await ActionStore().ReadEvidenceAsync(candidate, true, default))!;

        Assert.Null(evidence.BillText);
    }

    [Fact]
    public async Task ARevisionDraftedFromTheBillTextReadsBackWithItsCitationsAndTheDatabaseRefusesACitationToTheSummaryBesideThem()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);
        var revision = TextRevision(candidate, stored);
        await SettleTextRevisionAsync(candidate, revision);
        var reviews = ActionReviews();

        var found = await reviews.GetRevisionAsync(RecordId, 1, default);
        var current = (await ActionStore().ReadEvidenceAsync(candidate, false, default))!.Current;

        var expected = JsonSerializer.SerializeToNode(revision, ActionReaderJson);
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(found, ActionReaderJson)), "The review store changed the revision.");
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(current, ActionReaderJson)), "The draft store changed the revision.");
        Assert.Equal(revision.TextCitations.Select((citation, position) =>
                $"{position}|{citation.Part}|{citation.Span.ExtractionId}|{citation.Span.Start}|{citation.Span.Length}|{citation.Sentence}"),
            await LinesAsync("SELECT position || '|' || part || '|' || citation_extraction_id || '|' || citation_start || '|' || citation_length || '|' || sentence " +
                $"FROM action_revision_text_citations WHERE record_id = '{RecordId}' AND revision_number = 1 ORDER BY position"));
        Assert.Equal($"BILLS-119hr7ih|{revision.WhyItMatters}",
            await ScalarAsync($"SELECT text_package_id || '|' || why_it_matters FROM action_revisions WHERE record_id = '{RecordId}'"));

        var both = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            $"UPDATE action_revisions SET summary_version = 'v', summary_citation_extraction_id = '{stored.ExtractionId}', summary_citation_start = 0, summary_citation_length = 5"));
        var half = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "UPDATE action_revisions SET why_it_matters = NULL"));
        var missing = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            "UPDATE action_revisions SET text_package_id = 'BILLS-119hr7zz'"));
        Assert.Equal(("ck_action_revisions_text_source", "ck_action_revisions_why_it_matters"), (both.ConstraintName, half.ConstraintName));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, missing.SqlState);
    }

    [Fact]
    public async Task KeyTermsReadBackInOrderWithTheirExactSpansAndTheDatabaseRefusesATermPastFiveARepeatedTermAndABlankDefinition()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.BodyWithDefinitions);
        var revision = KeyTermRevision(candidate, stored);
        await SettleTextRevisionAsync(candidate, revision);

        var found = await ActionReviews().GetRevisionAsync(RecordId, 1, default);
        var current = (await ActionStore().ReadEvidenceAsync(candidate, false, default))!.Current;

        Assert.Equal([.. revision.KeyTerms], [.. found!.KeyTerms]);
        Assert.Equal([.. revision.KeyTerms], [.. current!.KeyTerms]);
        Assert.Equal(revision.KeyTerms.Select((term, position) =>
                $"{position}|{term.Term}|{term.Span.ExtractionId}|{term.Span.Start}|{term.Span.Length}|{term.Definition}|{term.Cut}"),
            await LinesAsync("SELECT position || '|' || term || '|' || citation_extraction_id || '|' || citation_start || '|' || citation_length || '|' || definition || '|' || " +
                $"CASE WHEN cut THEN 'True' ELSE 'False' END FROM action_revision_key_terms WHERE record_id = '{RecordId}' AND revision_number = 1 ORDER BY position"));
        Assert.All(found.KeyTerms, term => Assert.DoesNotContain("``", term.Definition, StringComparison.Ordinal));
        var first = revision.KeyTerms[0];
        var insert = $"INSERT INTO action_revision_key_terms (record_id, revision_number, position, term, citation_extraction_id, citation_start, citation_length, definition, cut) " +
            $"VALUES ('{RecordId}', 1, {{0}}, '{{1}}', '{first.Span.ExtractionId}', {first.Span.Start}, {first.Span.Length}, '{{2}}', false)";
        var sixth = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, string.Format(insert, 5, "Sixth", "The term Sixth means six.")));
        var repeated = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, string.Format(insert, 4, first.Term.ToUpperInvariant(), "The term means it.")));
        var blank = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, string.Format(insert, 4, "Fifth", "   ")));
        Assert.Equal(("ck_action_revision_key_terms_position", "ux_action_revision_key_terms_term", "ck_action_revision_key_terms_quote"),
            (sixth.ConstraintName, repeated.ConstraintName, blank.ConstraintName));
    }

    [Fact]
    public async Task CodeApprovesATextDraftInTheSameTransactionAfterItsChecksPassSoItIsReadyToPublish()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);

        await SettleTextRevisionAsync(candidate, TextRevision(candidate, stored));

        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM action_drafts"));
        Assert.Equal("civic-lens:code", await ScalarAsync("SELECT actor_subject FROM action_review_decisions"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT held_reason FROM action_drafts"));
    }

    [Fact]
    public async Task CodeApprovesATextDraftWithKeyTermsAfterItChecksTheirQuotesDefinitionsAndCutMarks()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.BodyWithDefinitions);

        await SettleTextRevisionAsync(candidate, KeyTermRevision(candidate, stored));

        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM action_drafts"));
        Assert.Equal("civic-lens:code", await ScalarAsync("SELECT actor_subject FROM action_review_decisions"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_revision_key_terms"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revision_key_terms WHERE definition LIKE '%\"Secretary\"%'"));
    }

    [Fact]
    public async Task PublicationRefusesBillTextCitationsFromAnExtractionOtherThanTheOneForTheStoredTextVersion()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);
        await SettleTextRevisionAsync(candidate, TextRevision(candidate, stored));
        var otherJob = await EnqueueDetailAsync("other-text-extraction");
        var otherExtraction = await ExtractionAsync(otherJob, stored.Reading.Text, stored.Reading.Text, ActionBillTexts.ExtractionVersion);
        await ExecuteAsync(connectionString, $"UPDATE action_revision_text_citations SET citation_extraction_id = '{otherExtraction}'");

        await Assert.ThrowsAsync<InvalidDataException>(() => ActionReviews().GetForPublicationAsync([RecordId], default));
    }

    [Fact]
    public async Task ATextDraftAboveTheGradeLimitWaitsForAPersonWithTheReasonKeptBecauseCodeApprovesOnlyReadableText()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);
        var hard = "Comprehensive reauthorization notwithstanding administrative reconsideration necessitates multidimensional intergovernmental accountability.";
        var revision = TextRevision(candidate, stored);
        var citations = revision.TextCitations.SetItem(0, revision.TextCitations[0] with { Sentence = hard });

        await SettleTextRevisionAsync(candidate, revision with { TextCitations = citations, Summary = ActionTextCitation.Join(citations.Where(item => item.Part == ActionTextPart.Summary)) });

        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM action_drafts"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
        Assert.Contains("read at grade", (string)(await ScalarAsync("SELECT held_reason FROM action_drafts"))!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATextRevisionMovesToColumnsWithItsTextCitationsAndVerifyFindsNoMismatchBecauseTheBackfillRebuildsThemFromTheJson()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);
        var revision = TextRevision(candidate, stored);
        await SettleTextRevisionAsync(candidate, revision);
        await MakeActionRevisionsLegacyAsync(1);
        await ExecuteAsync(connectionString, "DELETE FROM action_revision_text_citations; UPDATE action_revisions SET text_package_id = NULL, why_it_matters = NULL");

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal($"BILLS-119hr7ih|{revision.WhyItMatters}",
            await ScalarAsync("SELECT text_package_id || '|' || why_it_matters FROM action_revisions"));
        Assert.Equal(revision.TextCitations.Select(citation => citation.Sentence),
            await LinesAsync("SELECT sentence FROM action_revision_text_citations ORDER BY position"));
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    private async Task<(string ExtractionId, CongressBillTextReading Reading)> StoreBillTextAsync(string candidate, string text)
    {
        var detail = (await CandidatesAsync()).Single(item => item.CandidateId == candidate).DetailJobId;
        var textJob = await EnqueueDetailAsync("text-source");
        var reading = CongressBillRenderer.RenderText(new CongressBillListItem(119, "hr", 7, default), IntroducedText, text);
        var extractionId = await ExtractionAsync(textJob, reading.Text, reading.Text, ActionBillTexts.ExtractionVersion);
        await ExecuteAsync(connectionString, $"""
            INSERT INTO bill_text_versions (congress, bill_type, bill_number, package_id, version_type, version_date, source_url, extraction_id, chars_total, chars_read)
            VALUES (119, 'hr', 7, 'BILLS-119hr7ih', 'Introduced in House', '2025-03-01', '{BillTextFixtures.CongressUrl()}', '{extractionId}', {reading.CharsTotal}, {reading.CharsRead});
            UPDATE bill_gates SET text_state = 'Read', text_package_id = 'BILLS-119hr7ih' WHERE detail_job_id = '{detail}'
            """);
        return (extractionId, reading);
    }

    private static ActionDraftRevision TextRevision(string candidate, (string ExtractionId, CongressBillTextReading Reading) stored)
    {
        ActionTextCitation Cite(ActionTextPart part, string quote, string sentence) =>
            new(part, new(stored.ExtractionId, stored.Reading.Text.IndexOf(quote, StringComparison.Ordinal), quote.Length), sentence);
        var basis = Revision(candidate, 1);
        return basis with
        {
            Summary = $"{ActionBillTexts.FirstPlain} {ActionBillTexts.SecondPlain}",
            TextPackageId = "BILLS-119hr7ih",
            WhyItMatters = ActionBillTexts.WhyPlain,
            TextCitations =
            [
                Cite(ActionTextPart.Summary, ActionBillTexts.FirstQuote, ActionBillTexts.FirstPlain),
                Cite(ActionTextPart.Summary, ActionBillTexts.SecondQuote, ActionBillTexts.SecondPlain),
                Cite(ActionTextPart.WhyItMatters, ActionBillTexts.WhyQuote, ActionBillTexts.WhyPlain)
            ],
            Sponsorships = [.. basis.Sponsorships.Select(item => item with { Citations = [new(stored.ExtractionId, 0, 5)] })]
        };
    }

    private static ActionDraftRevision KeyTermRevision(string candidate, (string ExtractionId, CongressBillTextReading Reading) stored)
    {
        ActionKeyTerm Term(string term, string definition) =>
            new(term, new(stored.ExtractionId, stored.Reading.Text.IndexOf(definition, StringComparison.Ordinal), definition.Length),
                GpoQuoteMarks.Normalize(definition), Cut: false);
        return TextRevision(candidate, stored) with
        {
            KeyTerms = [Term("Secretary", ActionBillTexts.SecretaryDefinition), Term("eligible tenant", ActionBillTexts.TenantDefinition)]
        };
    }

    private async Task SettleTextRevisionAsync(string candidate, ActionDraftRevision revision)
    {
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        Assert.True(await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, revision, 0), DraftedRun(runId, candidate), default));
    }
}
