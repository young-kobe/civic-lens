using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class ReleaseHomeIssueCounts : Migration
{
    private const string UpSql = """
        ALTER TABLE release_issues ADD COLUMN count_congress integer NULL;
        ALTER TABLE release_issues ADD COLUMN substantive_votes bigint NULL;
        ALTER TABLE release_issues ADD COLUMN bills bigint NULL;
        ALTER TABLE release_issues ADD CONSTRAINT ck_release_issues_home_counts
            CHECK ((count_congress IS NULL AND substantive_votes IS NULL AND bills IS NULL) OR
                   (count_congress IS NOT NULL AND substantive_votes IS NOT NULL AND bills IS NOT NULL AND
                    count_congress > 0 AND substantive_votes >= 0 AND bills >= 0));

        WITH release_days AS (
            SELECT r.release_number,
                   DATE '0001-01-01' + (r.published_at_utc_ticks / 864000000000)::integer AS published_day
              FROM publication_releases r
        ), release_congress AS (
            SELECT release_number,
                   CASE WHEN published_day < DATE '1935-01-03' THEN NULL
                        ELSE ((CASE WHEN extract(month FROM published_day) = 1 AND extract(day FROM published_day) < 3
                                    THEN extract(year FROM published_day)::integer - 1
                                    ELSE extract(year FROM published_day)::integer END - 1789) / 2 + 1)
                    END AS congress
              FROM release_days
        ), vote_counts AS (
            SELECT rc.release_number, vi.issue_id, count(*) AS total
              FROM release_congress rc
              JOIN publication_record_states s ON s.first_release <= rc.release_number
                   AND (s.last_release IS NULL OR s.last_release >= rc.release_number) AND s.kind = 'vote'
              JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
              JOIN vote_revision_issues vi ON vi.record_id = v.record_id AND vi.revision_number = v.revision_number
             WHERE rc.congress IS NOT NULL AND v.congress = rc.congress AND v.member_list_id IS NOT NULL AND vi.is_tracked
               AND v.kind IN ('Passage', 'Suspension', 'Amendment', 'ConferenceReport', 'VetoOverride', 'Nomination')
             GROUP BY rc.release_number, vi.issue_id
        ), bill_counts AS (
            SELECT rc.release_number, ai.issue_id, count(*) AS total
              FROM release_congress rc
              JOIN publication_record_states s ON s.first_release <= rc.release_number
                   AND (s.last_release IS NULL OR s.last_release >= rc.release_number) AND s.kind = 'action'
              JOIN action_revisions a ON a.record_id = s.record_id AND a.revision_number = s.revision_number
              JOIN action_revision_issues ai ON ai.record_id = a.record_id AND ai.revision_number = a.revision_number
             WHERE rc.congress IS NOT NULL AND a.congress = rc.congress AND a.headline IS NOT NULL AND a.event_on IS NOT NULL AND ai.is_tracked
               AND EXISTS (SELECT 1 FROM action_revision_sponsorships p
                            WHERE p.record_id = a.record_id AND p.revision_number = a.revision_number)
             GROUP BY rc.release_number, ai.issue_id
        ), snapshots AS (
            SELECT names.release_number, names.issue_id, rc.congress,
                   COALESCE(v.total, 0) AS votes, COALESCE(b.total, 0) AS bills
              FROM release_issues names
              JOIN release_congress rc ON rc.release_number = names.release_number
              LEFT JOIN vote_counts v ON v.release_number = names.release_number AND v.issue_id = names.issue_id
              LEFT JOIN bill_counts b ON b.release_number = names.release_number AND b.issue_id = names.issue_id
             WHERE rc.congress IS NOT NULL
        )
        UPDATE release_issues i
           SET count_congress = s.congress, substantive_votes = s.votes, bills = s.bills
          FROM snapshots s
         WHERE i.release_number = s.release_number AND i.issue_id = s.issue_id;

        CREATE OR REPLACE VIEW public_api.release_issues WITH (security_barrier = true) AS
        SELECT i.release_number, i.issue_id, i.name, i.definition, i.count_congress, i.substantive_votes, i.bills
          FROM public.release_issues i JOIN public.publication_releases r ON r.release_number = i.release_number
         WHERE r.is_active;
        ALTER VIEW public_api.release_issues OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = """
        DROP VIEW public_api.release_issues;
        CREATE VIEW public_api.release_issues WITH (security_barrier = true) AS
        SELECT i.release_number, i.issue_id, i.name, i.definition
          FROM public.release_issues i JOIN public.publication_releases r ON r.release_number = i.release_number
         WHERE r.is_active;
        ALTER VIEW public_api.release_issues OWNER TO civic_lens_public_owner;
        ALTER TABLE release_issues DROP CONSTRAINT ck_release_issues_home_counts;
        ALTER TABLE release_issues DROP COLUMN count_congress, DROP COLUMN substantive_votes, DROP COLUMN bills;
        GRANT SELECT ON public_api.release_issues TO civic_lens_public;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
