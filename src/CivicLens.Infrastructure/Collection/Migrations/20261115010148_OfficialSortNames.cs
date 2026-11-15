using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <summary>
    /// Adds the roster last name to <c>roster_members</c> and <c>executive_officials</c>, and the sort name to <c>release_officials</c>. Null means that no writer filled the row yet,
    /// or that no roster names the official. The view <c>public_api.officials</c> shows <c>sort_name</c> last, the stored sort name else the name, so the view keeps its grants.
    /// <c>PublicApiGrants</c> grants the column to the view owner. <c>releases columns backfill</c> fills the rows that earlier imports and releases wrote.
    /// </summary>
    public partial class OfficialSortNames : Migration
    {
        private const string UpOfficialsSql = """
            CREATE OR REPLACE VIEW public_api.officials WITH (security_barrier = true) AS
            SELECT r.release_number, o.official_id, o.name, t.kind, t.office, t.state, t.district, t.party, t.caucus, t.end_on AS term_end_on,
                   COALESCE(c.record_count, 0) AS record_count, o.photo_path,
                   COALESCE(o.sort_name, o.name) AS sort_name
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
            """;

        // PostgreSQL cannot remove an appended view column in place. Recreate the view of PublicApiPartyViews on rollback.
        private const string DownOfficialsSql = """
            DROP VIEW public_api.officials;

            CREATE VIEW public_api.officials WITH (security_barrier = true) AS
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

            ALTER VIEW public_api.officials OWNER TO civic_lens_public_owner;
            GRANT SELECT ON public_api.officials TO civic_lens_public;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "roster_members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sort_name",
                table: "release_officials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "executive_officials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_roster_members_last_name",
                table: "roster_members",
                sql: "last_name IS NULL OR length(btrim(last_name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_officials_sort_name",
                table: "release_officials",
                sql: "sort_name IS NULL OR length(btrim(sort_name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_officials_last_name",
                table: "executive_officials",
                sql: "last_name IS NULL OR length(btrim(last_name)) > 0");

            migrationBuilder.Sql(UpOfficialsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownOfficialsSql);

            migrationBuilder.DropCheckConstraint(
                name: "ck_roster_members_last_name",
                table: "roster_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_officials_sort_name",
                table: "release_officials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_officials_last_name",
                table: "executive_officials");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "roster_members");

            migrationBuilder.DropColumn(
                name: "sort_name",
                table: "release_officials");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "executive_officials");
        }
    }
}
