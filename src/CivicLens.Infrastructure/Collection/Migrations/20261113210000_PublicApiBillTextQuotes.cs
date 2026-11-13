using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the view <c>public_api.bill_text_sentences</c>: each sentence of the bill text summary of an active bill record, with its part and the exact
/// characters of its quote. It adds the column <c>quote</c> to <c>public_api.bill_key_terms</c>, with the exact characters of each definition quote.
/// Both views serve rows for the origin Model only, the origin that carries the AI label, so they share one gate. The rollback drops the new view and
/// rebuilds the key term view of <c>ActionRevisionKeyTerms</c>, because a replaced view cannot drop a column.
/// </summary>
public partial class PublicApiBillTextQuotes : Migration
{
    private const string Supplementary = @"[\U00010000-\U0010FFFF]";

    // A span counts UTF-16 units, and substr counts code points. Each supplementary character is one code point and two UTF-16 units. The slice
    // doubles each such character in the prefix, so positions count UTF-16 units, and then halves each pair again. A span never splits a pair, so
    // the slice holds whole pairs only. The prefix in code points is at least as long as the span end in UTF-16 units.
    private static string QuoteOf(string text, string start, string length) => $"""
        regexp_replace(substr(regexp_replace(left({text}, {start} + {length}), '({Supplementary})', '\1\1', 'g'), {start} + 1, {length}),
                       '({Supplementary})\1', '\1', 'g')
        """;

    // The bill records whose active revision is model text drafted from the bill text. The same gate serves both views.
    private const string ModelTextRevisions = """
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
        """;

    private const string ModelTextGate = """
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND r.text_origin = 'Model' AND r.text_package_id IS NOT NULL
        """;

    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.bill_key_terms WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, k."position", k.term, k.definition, k.cut,
               {ActionRevisionKeyTerms.KeyTermsLabel} AS label,
               {QuoteOf("d.text", "k.citation_start", "k.citation_length")} AS quote
        {ModelTextRevisions}
          JOIN public.action_revision_key_terms k ON k.record_id = r.record_id AND k.revision_number = r.revision_number
          JOIN public.document_extractions d ON d.extraction_id = k.citation_extraction_id
        {ModelTextGate};

        CREATE VIEW public_api.bill_text_sentences WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, x."position", x.part, x.sentence,
               {QuoteOf("d.text", "x.citation_start", "x.citation_length")} AS quote
        {ModelTextRevisions}
          JOIN public.action_revision_text_citations x ON x.record_id = r.record_id AND x.revision_number = r.revision_number
          JOIN public.document_extractions d ON d.extraction_id = x.citation_extraction_id
        {ModelTextGate};

        ALTER VIEW public_api.bill_text_sentences OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        DROP VIEW public_api.bill_text_sentences;
        DROP VIEW public_api.bill_key_terms;
        {ActionRevisionKeyTerms.ViewSql}
        REVOKE SELECT (citation_extraction_id, citation_start, citation_length) ON action_revision_key_terms FROM civic_lens_public_owner;
        REVOKE SELECT (part, sentence, citation_start, citation_length) ON action_revision_text_citations FROM civic_lens_public_owner;
        """);
}
