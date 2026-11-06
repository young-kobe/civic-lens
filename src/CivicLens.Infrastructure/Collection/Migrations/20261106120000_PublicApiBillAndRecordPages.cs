using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiBillAndRecordPages : Migration
    {
        // Each view joins public_api.active_records and the base tables itself, as public_api.member_votes does.
        // bill_status shows the same bills as public_api.bills, so a child view never shows a record that bills hides.
        // bill_status takes the newest gate row of the bill and then drops a row with no text, as IActionReviewStore.ReadBillStatusesAsync does.
        // record_links gives each relationship two rows: one for the page of the statement and one for the page of the target.
        // Both ends and the relationship must be active, so no row names a record that the release does not hold.
        private const string UpSql = """
            CREATE VIEW public_api.bill_status WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, g.latest_action_text, g.latest_action_date, g.created_at_utc_ticks AS read_at_utc_ticks
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT x.latest_action_text, x.latest_action_date, x.created_at_utc_ticks
                     FROM public.bill_gates x
                    WHERE x.source_id = r.source_id AND x.congress = r.congress AND x.bill_type = r.bill_type AND x.bill_number = r.bill_number
                    ORDER BY x.update_date_utc_ticks DESC LIMIT 1) g
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND g.latest_action_text IS NOT NULL
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            CREATE VIEW public_api.record_links WITH (security_barrier = true) AS
            SELECT s.record_id, 'statement' AS kind,
                   t.kind AS related_kind, t.record_id AS related_record_id, te.headline AS related_headline,
                   l.link_type, l.event_date AS statement_date, a.record_id AS link_record_id
              FROM public_api.active_records a
              JOIN public.published_entries l ON l.kind = 'relationship' AND l.record_id = a.record_id AND l.revision_number = a.revision_number
              JOIN public_api.active_records s ON s.kind = 'statement' AND s.record_id = l.statement_record_id
              JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
              JOIN public.published_entries te ON te.kind = t.kind AND te.record_id = t.record_id AND te.revision_number = t.revision_number
             WHERE a.kind = 'relationship'
            UNION ALL
            SELECT t.record_id, t.kind,
                   'statement', s.record_id, se.headline,
                   l.link_type, l.event_date, a.record_id
              FROM public_api.active_records a
              JOIN public.published_entries l ON l.kind = 'relationship' AND l.record_id = a.record_id AND l.revision_number = a.revision_number
              JOIN public_api.active_records s ON s.kind = 'statement' AND s.record_id = l.statement_record_id
              JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
              JOIN public.published_entries se ON se.kind = 'statement' AND se.record_id = s.record_id AND se.revision_number = s.revision_number
             WHERE a.kind = 'relationship';

            CREATE VIEW public_api.vote_facts WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, f."position", f.fact_text
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_facts f ON f.record_id = v.record_id AND f.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            CREATE VIEW public_api.executive_actions WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   r.title AS headline, r.kind, r.document_number, r.signed_on, r.published_on, r.federal_register_citation, r.document_url,
                   r.person_id AS official_id, r.president_name
              FROM public_api.active_records a
              JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;

            CREATE VIEW public_api.executive_action_notices WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, n."position", n.document_number, d.published_on, d.notice_url
              FROM public_api.active_records a
              JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.executive_action_revision_notices n ON n.record_id = r.record_id AND n.revision_number = r.revision_number
              JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
             WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;

            ALTER VIEW public_api.bill_status OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.record_links OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_facts OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.executive_actions OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.executive_action_notices OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.executive_action_notices, public_api.executive_actions, public_api.vote_facts,
                public_api.record_links, public_api.bill_status;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
