using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiPartyViews : Migration
    {
        // New columns are appended so existing view column positions and SELECT grants remain stable.
        // Member caucus comes from the active release's term on the vote date and in that vote's chamber.
        private const string UpSql = """
            CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline, v.summary, v.text_origin,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   CASE WHEN t.other_casts = 0 THEN t.yea END AS yea,
                   CASE WHEN t.other_casts = 0 THEN t.nay END AS nay,
                   CASE WHEN t.other_casts = 0 THEN t.present END AS present,
                   CASE WHEN t.other_casts = 0 THEN t.not_voting END AS not_voting,
                   v.question, v.result,
                   v.alignment
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                          count(*) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                          count(*) FILTER (WHERE e.vote_cast = 'Present') AS present,
                          count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting,
                          count(*) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting')) AS other_casts
                     FROM public.vote_member_list_entries e WHERE e.list_id = v.member_list_id) t
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            CREATE OR REPLACE VIEW public_api.vote_members WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, e."position", e.person_id AS official_id, e.vote_cast,
                   e.party,
                   term.caucus
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
              LEFT JOIN LATERAL (
                   SELECT x.caucus
                     FROM public.release_official_terms x
                    WHERE x.release_number = a.release_number AND x.official_id = e.person_id
                      AND x.start_on <= v.voted_on AND (x.end_on IS NULL OR x.end_on >= v.voted_on)
                      AND ((v.chamber = 'House' AND x.kind = 'Representative') OR (v.chamber = 'Senate' AND x.kind = 'Senator'))
                    ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) term ON TRUE
             WHERE a.kind = 'vote';

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
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids,
                   CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' WHEN 'Nay' THEN 'Nay' WHEN 'No' THEN 'Nay' END AS side,
                   CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END AS party_majority_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE OR REPLACE VIEW public_api.officials WITH (security_barrier = true) AS
            SELECT r.release_number, o.official_id, o.name, t.kind, t.office, t.state, t.district, t.party, t.caucus, t.end_on AS term_end_on,
                   COALESCE(c.record_count, 0) AS record_count, o.photo_path
              FROM public.publication_releases r
              JOIN public.release_officials o ON o.release_number = r.release_number
              LEFT JOIN LATERAL (
                   SELECT x.kind, x.office, x.state, x.district, x.party, x.caucus, x.end_on
                     FROM public.release_official_terms x
                    WHERE x.release_number = o.release_number AND x.official_id = o.official_id
                      AND x.start_on <= DATE '0001-01-01' + (r.published_at_utc_ticks / 864000000000)::int
                    ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) t ON TRUE
              LEFT JOIN (
                   SELECT e.official_id, count(*) AS record_count
                     FROM public_api.active_records a
                     JOIN public.published_entry_officials e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
                    GROUP BY e.official_id) c ON c.official_id = o.official_id
             WHERE r.is_active;

            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.officials OWNER TO civic_lens_public_owner;
            """;

        // PostgreSQL cannot remove appended view columns in place. Recreate the four prior definitions on rollback.
        private const string DownSql = """
            DROP VIEW public_api.member_votes, public_api.vote_members, public_api.votes, public_api.officials;

            CREATE VIEW public_api.votes WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline, v.summary, v.text_origin,
                   CASE v.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END AS label,
                   CASE WHEN t.other_casts = 0 THEN t.yea END AS yea,
                   CASE WHEN t.other_casts = 0 THEN t.nay END AS nay,
                   CASE WHEN t.other_casts = 0 THEN t.present END AS present,
                   CASE WHEN t.other_casts = 0 THEN t.not_voting END AS not_voting,
                   v.question, v.result
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                          count(*) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                          count(*) FILTER (WHERE e.vote_cast = 'Present') AS present,
                          count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting,
                          count(*) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting')) AS other_casts
                     FROM public.vote_member_list_entries e WHERE e.list_id = v.member_list_id) t
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, e."position", e.person_id AS official_id, e.vote_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
            SELECT e.person_id AS official_id, a.record_id,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline,
                   CASE v.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END AS label,
                   v.result, e.vote_cast,
                   ARRAY(SELECT i.issue_id::text FROM public.vote_revision_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE VIEW public_api.officials WITH (security_barrier = true) AS
            SELECT r.release_number, o.official_id, o.name, t.kind, t.office, t.state, t.district, t.party, t.caucus, t.end_on AS term_end_on,
                   COALESCE(c.record_count, 0) AS record_count
              FROM public.publication_releases r
              JOIN public.release_officials o ON o.release_number = r.release_number
              LEFT JOIN LATERAL (
                   SELECT x.kind, x.office, x.state, x.district, x.party, x.caucus, x.end_on
                     FROM public.release_official_terms x
                    WHERE x.release_number = o.release_number AND x.official_id = o.official_id
                      AND x.start_on <= DATE '0001-01-01' + (r.published_at_utc_ticks / 864000000000)::int
                    ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) t ON TRUE
              LEFT JOIN (
                   SELECT e.official_id, count(*) AS record_count
                     FROM public_api.active_records a
                     JOIN public.published_entry_officials e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
                    GROUP BY e.official_id) c ON c.official_id = o.official_id
             WHERE r.is_active;

            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.officials OWNER TO civic_lens_public_owner;
            GRANT SELECT ON public_api.votes, public_api.vote_members, public_api.member_votes, public_api.officials TO civic_lens_public;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
