using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <summary>
    /// Adds the column <c>status</c> to <c>release_bill_statuses</c>: the name of <c>BillStatusStage</c>, which C# writes with <c>BillStatusMeaning</c>.
    /// Null means that no writer filled the row yet. The view <c>public_api.bill_status</c> shows the column last, so the view keeps its grants.
    /// <c>PublicApiGrants</c> grants the column to the view owner. The rollback restores the view of <c>ReleaseBillStatuses</c> and drops the column.
    /// </summary>
    public partial class ReleaseBillStatusStage : Migration
    {
        private const string UpBillStatusSql = """
            CREATE OR REPLACE VIEW public_api.bill_status WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, s.latest_action_text, s.latest_action_date, s.read_at_utc_ticks, s.status
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.release_bill_statuses s ON s.release_number = a.release_number AND s.record_id = a.record_id
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);
            """;

        private const string DownBillStatusSql = """
            DROP VIEW public_api.bill_status;

            CREATE VIEW public_api.bill_status WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, s.latest_action_text, s.latest_action_date, s.read_at_utc_ticks
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.release_bill_statuses s ON s.release_number = a.release_number AND s.record_id = a.record_id
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.bill_status OWNER TO civic_lens_public_owner;
            GRANT SELECT ON public_api.bill_status TO civic_lens_public;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "release_bill_statuses",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(UpBillStatusSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownBillStatusSql);

            migrationBuilder.DropColumn(
                name: "status",
                table: "release_bill_statuses");
        }
    }
}
