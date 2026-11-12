using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Lets the entry of a relationship name who approved its published revision, as the entry of a statement does. The view <c>public_api.record_links</c>
/// gains the column <c>approver</c>, which is null until the entries backfill fills the relationship entries that already exist.
/// </summary>
public partial class RelationshipApprover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_published_entries_approved_by", table: "published_entries");
        migrationBuilder.AddCheckConstraint(name: "ck_published_entries_approved_by", table: "published_entries",
            sql: "approved_by IS NULL OR (kind IN ('statement', 'relationship') AND approved_by IN ('Code', 'Person'))");
        migrationBuilder.Sql($"""
            CREATE OR REPLACE VIEW public_api.record_links WITH (security_barrier = true) AS
            SELECT l.statement_record_id AS record_id, 'statement' AS kind,
                   t.kind AS related_kind, t.record_id AS related_record_id, h.headline AS related_headline, h.text_origin AS related_text_origin, h.label AS related_label,
                   {PublicApiSearchSources.LinkColumns}, l.approved_by AS approver
            {PublicApiSearchSources.LinkJoins}
              CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = t.kind AND x.record_id = t.record_id OFFSET 0) h
             WHERE l.kind = 'relationship' AND l.statement_kind = 'statement'
            UNION ALL
            SELECT l.target_record_id, l.target_kind,
                   'statement', s.record_id, h.headline, h.text_origin, h.label,
                   {PublicApiSearchSources.LinkColumns}, l.approved_by
            {PublicApiSearchSources.LinkJoins}
              CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = l.statement_kind AND x.record_id = l.statement_record_id OFFSET 0) h
             WHERE l.kind = 'relationship' AND l.statement_kind = 'statement';
            """);
    }

    // A view cannot lose a column in place, so the rollback rebuilds it and grants it again.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            DROP VIEW public_api.record_links;
            {PublicApiSearchSources.RecordLinks}
            GRANT SELECT ON public_api.record_links TO civic_lens_public;
            """);
        migrationBuilder.DropCheckConstraint(name: "ck_published_entries_approved_by", table: "published_entries");
        migrationBuilder.AddCheckConstraint(name: "ck_published_entries_approved_by", table: "published_entries",
            sql: "approved_by IS NULL OR (kind = 'statement' AND approved_by IN ('Code', 'Person'))");
    }
}
