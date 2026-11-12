using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the views of the AI summary of an executive action. <c>public_api.executive_action_summaries</c> has one row for each active revision that
/// holds a summary. Its <c>text_origin</c> is the origin of the summary. Its <c>label</c> is set only for the origin Model, the origin that carries
/// the label. The label text copies <c>ExecutiveSummaryWords.Label</c>, because SQL cannot read the class. A test compares them.
/// <c>public_api.executive_action_summary_sentences</c> holds the sentences of those revisions in position order. A new view keeps
/// <c>executive_actions</c> unchanged, so the rollback only drops the two views.
/// </summary>
public partial class PublicApiExecutiveActionSummary : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.executive_action_summaries WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.summary_text_origin AS text_origin,
               CASE WHEN r.summary_text_origin = 'Model'
                    THEN 'An AI model summarized the document text. Code checks each quote.' END AS label,
               r.summary_cut AS cut
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL AND r.summary_text_origin IS NOT NULL;

        CREATE VIEW public_api.executive_action_summary_sentences WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", s.sentence
          FROM public_api.executive_action_summaries m
          JOIN public.executive_action_revision_summary_sentences s ON s.record_id = m.record_id AND s.revision_number = m.revision_number
         WHERE m.text_origin = 'Model';

        ALTER VIEW public_api.executive_action_summaries OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_summary_sentences OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.executive_action_summary_sentences;
        DROP VIEW public_api.executive_action_summaries;
        REVOKE SELECT (summary_text_origin, summary_cut) ON executive_action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, "position", sentence) ON executive_action_revision_summary_sentences FROM civic_lens_public_owner;
        """);
}
