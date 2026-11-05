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
    public async Task ACodeApprovalIsNeverGivenToATextDraftSoItWaitsForAPersonWithTheReasonKeptBecauseThePublishStepOwnsItsCitations()
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: false);
        var stored = await StoreBillTextAsync(candidate, ActionBillTexts.Body);

        await SettleTextRevisionAsync(candidate, TextRevision(candidate, stored));

        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM action_drafts"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
        Assert.Contains("waits for the publish step", (string)(await ScalarAsync("SELECT held_reason FROM action_drafts"))!, StringComparison.Ordinal);
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

    private async Task SettleTextRevisionAsync(string candidate, ActionDraftRevision revision)
    {
        var store = ActionStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        Assert.True(await store.CheckpointAsync(ActionDraftCheckpoint.From(claim, revision), DraftedRun(runId, candidate), default));
    }
}
