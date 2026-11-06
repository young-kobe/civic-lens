using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiIssuesHome : Migration
    {
        // Each view joins the base tables itself, as member_votes does, because Postgres does not flatten a security_barrier view into another one.
        // On 1,600 votes, 495,000 member rows, and 3,000 bills, a join of vote_issues with votes took about 220 ms for one issue. A join of bill_issues with bill_sponsorships ran over the 2 second limit.
        // The brief label repeats PublishedWording.ModelBrief, and a test compares the two. PublicApiGrants owns the grants.
        // An older brief revision holds revision_json and no columns, so it shows no row. The two issue views repeat the filters of vote_issues and bill_issues.
        // Only a model brief has a label. A brief with person text has none. A brief never has the text origin Code.
        private const string UpSql = """
            CREATE VIEW public_api.briefs WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number, r.week_start, r.headline, r.text_origin,
                   CASE r.text_origin WHEN 'Model' THEN 'Drafted by an AI model from reviewed records. A person checked it before publication.' END AS label
              FROM public_api.active_records a
              JOIN public.brief_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'brief' AND r.week_start IS NOT NULL AND r.headline IS NOT NULL AND r.text_origin IS NOT NULL;

            CREATE VIEW public_api.issue_votes WITH (security_barrier = true) AS
            SELECT i.issue_id, v.kind
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;

            CREATE VIEW public_api.issue_sponsors WITH (security_barrier = true) AS
            SELECT i.issue_id, p.person_id AS official_id, r.event_on AS event_date
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
              JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked AND p.role = 'Sponsor';

            ALTER VIEW public_api.briefs OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.issue_votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.issue_sponsors OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.briefs, public_api.issue_votes, public_api.issue_sponsors;
            REVOKE SELECT ON brief_revisions FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
