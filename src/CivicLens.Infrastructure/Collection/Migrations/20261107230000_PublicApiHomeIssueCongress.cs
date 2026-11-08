using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiHomeIssueCongress : Migration
    {
        private const string UpSql = """
            CREATE OR REPLACE VIEW public_api.issue_votes WITH (security_barrier = true) AS
            SELECT i.issue_id, v.kind, v.congress
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;

            CREATE OR REPLACE VIEW public_api.bill_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id, i."position", r.congress
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.issue_votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.bill_issues OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.issue_votes, public_api.bill_issues;

            CREATE VIEW public_api.issue_votes WITH (security_barrier = true) AS
            SELECT i.issue_id, v.kind
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;

            CREATE VIEW public_api.bill_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id, i."position"
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.issue_votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.bill_issues OWNER TO civic_lens_public_owner;
            GRANT SELECT ON public_api.issue_votes, public_api.bill_issues TO civic_lens_public;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
