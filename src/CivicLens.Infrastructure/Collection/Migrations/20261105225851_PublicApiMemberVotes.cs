using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiMemberVotes : Migration
    {
        // The view joins the base tables itself, because Postgres does not flatten a security_barrier view into another one.
        // A plan can start at the person index of vote_member_list_entries. It has no member count, so no count runs for each vote.
        // The label wording repeats the one of public_api.votes. PublicApiViewsTests compares both with PublishedRecordLabels.
        private const string UpSql = """
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
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP VIEW public_api.member_votes;");
    }
}
