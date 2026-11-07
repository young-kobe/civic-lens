using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiRecordLinkReason : Migration
{
    private const string UpSql = """
        CREATE OR REPLACE VIEW public_api.record_links WITH (security_barrier = true) AS
        SELECT l.statement_record_id AS record_id, 'statement' AS kind,
               t.kind AS related_kind, t.record_id AS related_record_id, h.headline AS related_headline, h.text_origin AS related_text_origin, h.label AS related_label,
               l.link_type, l.event_date AS statement_date, l.record_id AS link_record_id, r.reason
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          LEFT JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = t.kind AND x.record_id = t.record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement'
        UNION ALL
        SELECT l.target_record_id, l.target_kind,
               'statement', s.record_id, h.headline, h.text_origin, h.label,
               l.link_type, l.event_date, l.record_id, r.reason
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          LEFT JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = l.statement_kind AND x.record_id = l.statement_record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement';

        ALTER VIEW public_api.record_links OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = """
        DROP VIEW public_api.record_links;
        CREATE VIEW public_api.record_links WITH (security_barrier = true) AS
        SELECT l.statement_record_id AS record_id, 'statement' AS kind,
               t.kind AS related_kind, t.record_id AS related_record_id, h.headline AS related_headline, h.text_origin AS related_text_origin, h.label AS related_label,
               l.link_type, l.event_date AS statement_date, l.record_id AS link_record_id
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = t.kind AND x.record_id = t.record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement'
        UNION ALL
        SELECT l.target_record_id, l.target_kind,
               'statement', s.record_id, h.headline, h.text_origin, h.label,
               l.link_type, l.event_date, l.record_id
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = l.statement_kind AND x.record_id = l.statement_record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement';

        ALTER VIEW public_api.record_links OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.record_links TO civic_lens_public;
        REVOKE SELECT (record_id, revision_number, reason) ON relationship_revisions FROM civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
