using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiOfficials : Migration
    {
        // Every view joins base tables and public_api.active_records. No view reads another security_barrier view except active_records,
        // because Postgres does not flatten a stacked security_barrier view. A filter on official_id reaches ix_published_entry_officials_official_id, ix_vote_member_list_entries_person, and ix_action_revision_sponsorships_person.
        // The latest term is the newest term that started on or before the day of the release, as PublishedOfficialView.LatestTerm picks it.
        // 864000000000 is the number of ticks in one day.
        private const string UpSql = """
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

            CREATE VIEW public_api.official_attendance WITH (security_barrier = true) AS
            SELECT e.person_id AS official_id, v.chamber, v.congress,
                   count(*) AS held, count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS missed,
                   min(v.voted_on) AS first_voted_on, max(v.voted_on) AS last_voted_on
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote'
             GROUP BY e.person_id, v.chamber, v.congress;

            CREATE VIEW public_api.official_bill_counts WITH (security_barrier = true) AS
            SELECT p.person_id AS official_id, r.congress,
                   count(DISTINCT p.record_id) FILTER (WHERE p.role = 'Sponsor') AS sponsored,
                   count(DISTINCT p.record_id) FILTER (WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL AND NOT EXISTS (
                       SELECT 1 FROM public.action_revision_sponsorships s
                        WHERE s.record_id = p.record_id AND s.revision_number = p.revision_number AND s.person_id = p.person_id AND s.role = 'Sponsor')) AS cosponsored
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
             GROUP BY p.person_id, r.congress;

            CREATE VIEW public_api.official_issue_counts WITH (security_barrier = true) AS
            SELECT p.person_id AS official_id, r.congress, i.issue_id, 'bill'::text AS source, NULL::text AS vote_kind,
                   count(DISTINCT p.record_id) AS records
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number AND p.role = 'Sponsor'
              JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number AND i.is_tracked
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
             GROUP BY p.person_id, r.congress, i.issue_id
            UNION ALL
            SELECT e.person_id, v.congress, i.issue_id, 'vote'::text, v.kind::text, count(*)
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id AND e.vote_cast <> 'Not Voting'
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked
             WHERE a.kind = 'vote'
             GROUP BY e.person_id, v.congress, i.issue_id, v.kind;

            CREATE VIEW public_api.official_executive_actions WITH (security_barrier = true) AS
            SELECT e.official_id, p.document_type, count(*) AS actions
              FROM public_api.active_records a
              JOIN public.published_entries p ON p.kind = a.kind AND p.record_id = a.record_id AND p.revision_number = a.revision_number
              JOIN public.published_entry_officials e ON e.kind = p.kind AND e.record_id = p.record_id AND e.revision_number = p.revision_number
             WHERE a.kind = 'executiveAction'
             GROUP BY e.official_id, p.document_type;

            ALTER VIEW public_api.officials OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.official_attendance OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.official_bill_counts OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.official_issue_counts OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.official_executive_actions OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.officials, public_api.official_attendance, public_api.official_bill_counts,
                public_api.official_issue_counts, public_api.official_executive_actions;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_published_entry_officials_official_id",
                table: "published_entry_officials",
                column: "official_id");
            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);
            migrationBuilder.DropIndex(
                name: "ix_published_entry_officials_official_id",
                table: "published_entry_officials");
        }
    }
}
