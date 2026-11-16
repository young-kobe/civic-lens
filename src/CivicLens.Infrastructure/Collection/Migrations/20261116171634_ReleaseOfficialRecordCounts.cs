using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <summary>
    /// Adds <c>release_official_record_counts</c>, the record counts of each official that the publish writes once for each release, and its view.
    /// <c>public_api.officials</c> reads <c>record_count</c> from it instead of counting every active record on each read, so the view keeps its columns and grants.
    /// <c>PublicApiGrants</c> grants the columns to the view owner. <c>releases counts backfill</c> fills the releases that earlier publishes wrote.
    /// </summary>
    public partial class ReleaseOfficialRecordCounts : Migration
    {
        private const string ViewColumns = """
            SELECT r.release_number, o.official_id, o.name, t.kind, t.office, t.state, t.district, t.party, t.caucus, t.end_on AS term_end_on,
            """;

        private const string ViewTerms = """
              FROM public.publication_releases r
              JOIN public.release_officials o ON o.release_number = r.release_number
              LEFT JOIN LATERAL (
                   SELECT x.kind, x.office, x.state, x.district, x.party, x.caucus, x.end_on
                     FROM public.release_official_terms x
                    WHERE x.release_number = o.release_number AND x.official_id = o.official_id
                      AND x.start_on <= DATE '0001-01-01' + (r.published_at_utc_ticks / 864000000000)::int
                    ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) t ON TRUE
            """;

        // The view keeps the bigint type of record_count, so CREATE OR REPLACE keeps its grants.
        private const string UpSql = $$"""
            CREATE VIEW public_api.release_official_record_counts WITH (security_barrier = true) AS
            SELECT c.release_number, c.official_id, c.records, c.votes, c.bills, c.statements, c.executive_actions
              FROM public.release_official_record_counts c
              JOIN public.publication_releases r ON r.release_number = c.release_number
             WHERE r.is_active;

            ALTER VIEW public_api.release_official_record_counts OWNER TO civic_lens_public_owner;

            CREATE OR REPLACE VIEW public_api.officials WITH (security_barrier = true) AS
            {{ViewColumns}}
                   COALESCE(c.records, 0)::bigint AS record_count, o.photo_path,
                   COALESCE(o.sort_name, o.name) AS sort_name
            {{ViewTerms}}
              LEFT JOIN public.release_official_record_counts c ON c.release_number = o.release_number AND c.official_id = o.official_id
             WHERE r.is_active;
            """;

        // Restores the view of OfficialSortNames, which counts the records of every official on each read.
        private const string DownSql = $$"""
            CREATE OR REPLACE VIEW public_api.officials WITH (security_barrier = true) AS
            {{ViewColumns}}
                   COALESCE(c.record_count, 0) AS record_count, o.photo_path,
                   COALESCE(o.sort_name, o.name) AS sort_name
            {{ViewTerms}}
              LEFT JOIN (
                   SELECT e.official_id, count(*) AS record_count
                     FROM public_api.active_records a
                     JOIN public.published_entry_officials e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
                    GROUP BY e.official_id) c ON c.official_id = o.official_id
             WHERE r.is_active;

            DROP VIEW public_api.release_official_record_counts;
            REVOKE SELECT (release_number, official_id, records, votes, bills, statements, executive_actions)
                ON release_official_record_counts FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_official_record_counts",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    records = table.Column<int>(type: "integer", nullable: false),
                    votes = table.Column<int>(type: "integer", nullable: false),
                    bills = table.Column<int>(type: "integer", nullable: false),
                    statements = table.Column<int>(type: "integer", nullable: false),
                    executive_actions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_official_record_counts", x => new { x.release_number, x.official_id });
                    table.CheckConstraint("ck_release_official_record_counts_counts", "records >= 0 AND votes >= 0 AND bills >= 0 AND statements >= 0 AND executive_actions >= 0");
                    table.CheckConstraint("ck_release_official_record_counts_official_id", "length(btrim(official_id)) > 0");
                    table.ForeignKey(
                        name: "fk_release_official_record_counts_official",
                        columns: x => new { x.release_number, x.official_id },
                        principalTable: "release_officials",
                        principalColumns: new[] { "release_number", "official_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);

            migrationBuilder.DropTable(
                name: "release_official_record_counts");
        }
    }
}
