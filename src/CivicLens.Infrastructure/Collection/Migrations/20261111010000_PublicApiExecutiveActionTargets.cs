using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiExecutiveActionTargets : Migration
{
    // The stored span counts UTF-16 code units and substring counts code points. The text must therefore start with the stored
    // kind prefix and number, or the view gives no reference. A target links only when exactly one other active executive action has its kind and number.
    private const string UpSql = """
        CREATE VIEW public_api.executive_action_targets WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, t."position", t.relation, t.kind, t.number,
               CASE WHEN cited.text ~ ('^(EO|Proc\.) ' || t.number || '(, |$)') THEN cited.text END AS reference,
               (SELECT CASE WHEN count(*) = 1 THEN min(m.record_id) END
                  FROM public_api.active_records m
                  JOIN public.executive_action_revisions mr ON mr.record_id = m.record_id AND mr.revision_number = m.revision_number
                 WHERE m.kind = 'executiveAction' AND mr.title IS NOT NULL AND m.record_id <> a.record_id
                   AND mr.kind = t.kind AND mr.presidential_number = t.number) AS target_record_id
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.executive_action_revision_disposition_targets t ON t.record_id = r.record_id AND t.revision_number = r.revision_number
          CROSS JOIN LATERAL (SELECT substring(r.disposition_notes FROM t.reference_start + 1 FOR t.reference_length) AS text) cited
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;

        ALTER VIEW public_api.executive_action_targets OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.executive_action_targets;
        REVOKE SELECT (presidential_number, disposition_notes) ON executive_action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, "position", relation, kind, number, reference_start, reference_length)
            ON executive_action_revision_disposition_targets FROM civic_lens_public_owner;
        """);
}
