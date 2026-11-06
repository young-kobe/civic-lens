using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiMemberVoteIssues : Migration
    {
        // The issue filter of the member votes route reads this column. A filter on the view public_api.vote_issues made Postgres build the active records again for each row
        // (4.4 s on 1,600 votes). The column reads the base table by its primary key and only for the rows that the filter meets.
        // The new column goes last, because CREATE OR REPLACE VIEW keeps the existing columns and the grants.
        private const string UpSql = """
            CREATE OR REPLACE VIEW public_api.member_votes WITH (security_barrier = true) AS
            SELECT e.person_id AS official_id, a.record_id,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   v.result, e.vote_cast,
                   ARRAY(SELECT i.issue_id::text FROM public.vote_revision_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';
            """;

        // Postgres cannot drop a view column with CREATE OR REPLACE, so the down step creates the view as before.
        private const string DownSql = """
            DROP VIEW public_api.member_votes;
            CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
            SELECT e.person_id AS official_id, a.record_id,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   v.result, e.vote_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';
            ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
            GRANT SELECT ON public_api.member_votes TO civic_lens_public;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
