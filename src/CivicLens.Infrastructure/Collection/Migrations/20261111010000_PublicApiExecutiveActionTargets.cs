using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiExecutiveActionTargets : Migration
{
    private const string ExecutiveColumns = """
        SELECT a.record_id, a.revision_number, a.release_number,
               r.title AS headline, r.kind, r.document_number, r.signed_on, r.published_on, r.federal_register_citation, r.document_url,
               r.person_id AS official_id, r.president_name
        """;

    private const string ExecutiveFrom = """
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL
        """;

    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.executive_actions WITH (security_barrier = true) AS
        {ExecutiveColumns}, r.disposition_notes
        {ExecutiveFrom};

        CREATE VIEW public_api.executive_action_targets WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, t."position", t.relation, t.kind, t.number,
               t.reference_start, t.reference_length,
               (SELECT CASE WHEN count(*) = 1 THEN min(m.record_id) END
                  FROM public_api.active_records m
                  JOIN public.executive_action_revisions mr ON mr.record_id = m.record_id AND mr.revision_number = m.revision_number
                 WHERE m.kind = 'executiveAction' AND mr.title IS NOT NULL AND m.record_id <> a.record_id
                   AND mr.kind = t.kind AND mr.presidential_number = t.number) AS target_record_id
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.executive_action_revision_disposition_targets t ON t.record_id = r.record_id AND t.revision_number = r.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;

        ALTER VIEW public_api.executive_action_targets OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_executive_action_revisions_kind_presidential_number",
            table: "executive_action_revisions",
            columns: new[] { "kind", "presidential_number" });
        migrationBuilder.Sql(UpSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
        DROP VIEW public_api.executive_action_targets;
        DROP VIEW public_api.executive_actions;
        CREATE VIEW public_api.executive_actions WITH (security_barrier = true) AS
        {ExecutiveColumns}
        {ExecutiveFrom};
        ALTER VIEW public_api.executive_actions OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.executive_actions TO civic_lens_public;
        REVOKE SELECT (presidential_number, disposition_notes) ON executive_action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, "position", relation, kind, number, reference_start, reference_length)
            ON executive_action_revision_disposition_targets FROM civic_lens_public_owner;
        """);
        migrationBuilder.DropIndex(
            name: "IX_executive_action_revisions_kind_presidential_number",
            table: "executive_action_revisions");
    }
}
