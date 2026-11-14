using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Bills;

/// <summary>The bill text version that a text summary cites, and the characters of it that the model read. Each key term and sentence cites its quote.
/// The seed writes one extraction that holds every quote, and cites each quote by its UTF-16 span there.</summary>
internal sealed record SeededBillText(int CharsRead, int CharsTotal, (string Term, string Definition, bool Cut, string Quote)[]? KeyTerms = null,
    (string Part, string Sentence, string Quote)[]? Sentences = null);

internal sealed record SeededSponsorship(string Person, string Role, string SponsoredOn, string? WithdrawnOn = null, bool IsOriginal = true);

/// <summary>A published bill as the operational tables hold it. The public views read these rows.</summary>
internal sealed record SeededBill(char Id, string EventOn)
{
    public int Congress { get; init; } = 119;
    public string BillType { get; init; } = "hr";
    public int BillNumber { get; init; } = 7;
    public string? PolicyAreaId { get; init; } = "health";
    public string TextOrigin { get; init; } = "Model";
    public SeededSponsorship[] Sponsorships { get; init; } = [new("p1", "Sponsor", "2026-01-10"), new("p2", "Cosponsor", "2026-01-10")];
    public string[] Issues { get; init; } = [];
    public string[] UntrackedIssues { get; init; } = [];
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }
    public string RecordKind { get; init; } = "action";
    public string? BillLine { get; init; }
    public SeededBillText? Text { get; init; }

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class BillsSeed
{
    public static async Task AddBillsAsync(string database, params SeededBill[] bills)
    {
        foreach (var bill in bills) await ViewsSeed.ExecuteAsync(database, Insert(bill));
    }

    private static string EntryRow(SeededBill bill) => bill.BillLine is null ? "" : $"""
        INSERT INTO published_entries (kind, record_id, revision_number, action_record_id, action_revision_number, headline, bill_title, bill_line)
          VALUES ('action', '{bill.RecordId}', 1, '{bill.RecordId}', 1, 'H', 'T', '{bill.BillLine}');
        """;

    private static string TextRows(SeededBill bill)
    {
        if (bill.Text is null) return "";
        var package = $"BILLS-{bill.Congress}{bill.BillType}{bill.BillNumber}ih";
        var extraction = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bill.RecordId)));
        var keyTerms = bill.Text.KeyTerms ?? [];
        var sentences = bill.Text.Sentences ?? [];
        var text = "SEC. 1. " + string.Join(" ", keyTerms.Select(item => item.Quote).Concat(sentences.Select(item => item.Quote)));
        string Span(string quote) => $"'{extraction}', {text.IndexOf(quote, StringComparison.Ordinal)}, {quote.Length}";
        return $"""
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
              VALUES ('{extraction}', 'attempt', 'extraction', '{text}', '{new string('f', 64)}');
            INSERT INTO bill_text_versions (congress, bill_type, bill_number, package_id, version_type, version_date, source_url, extraction_id, chars_total, chars_read)
              VALUES ({bill.Congress}, '{bill.BillType}', {bill.BillNumber}, '{package}', 'Introduced in House', '2025-01-03',
                      'https://www.congress.gov/{bill.Congress}/bills/{bill.BillType}{bill.BillNumber}/{package}.htm', '{extraction}', {bill.Text.CharsTotal}, {bill.Text.CharsRead});
            UPDATE action_revisions SET text_package_id = '{package}', why_it_matters = 'It pays landlords.' WHERE record_id = '{bill.RecordId}';
            {string.Join("\n", keyTerms.Select((item, position) => $"INSERT INTO action_revision_key_terms (record_id, revision_number, position, term, citation_extraction_id, citation_start, citation_length, definition, cut) VALUES ('{bill.RecordId}', 1, {position}, '{item.Term}', {Span(item.Quote)}, '{item.Definition}', {(item.Cut ? "true" : "false")});"))}
            {string.Join("\n", sentences.Select((item, position) => $"INSERT INTO action_revision_text_citations (record_id, revision_number, position, part, citation_extraction_id, citation_start, citation_length, sentence) VALUES ('{bill.RecordId}', 1, {position}, '{item.Part}', {Span(item.Quote)}, '{item.Sentence}');"))}
            """;
    }

    private static string Insert(SeededBill bill)
    {
        var last = bill.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        var cosponsors = bill.Sponsorships.Count(item => item.Role == "Cosponsor");
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('{bill.RecordKind}', '{bill.RecordId}', {bill.FirstRelease}, 1, {last});
            {PublicActionRows.ActionRevision(bill.RecordId, 1, bill.TextOrigin, bill.EventOn, cosponsors, bill.Congress, bill.BillType, bill.BillNumber, bill.PolicyAreaId)}
            {string.Join('\n', bill.Sponsorships.Select((item, position) => PublicActionRows.ActionSponsorship(
                bill.RecordId, 1, position, item.Person, item.Role, item.IsOriginal, item.SponsoredOn, item.WithdrawnOn)))}
            {string.Join('\n', bill.Issues.Select((issue, position) => PublicActionRows.ActionIssue(bill.RecordId, 1, issue, position, true)))}
            {EntryRow(bill)}
            {TextRows(bill)}
            {string.Join('\n', bill.UntrackedIssues.Select((issue, position) => PublicActionRows.ActionIssue(bill.RecordId, 1, issue, bill.Issues.Length + position, false)))}
            """;
    }
}
