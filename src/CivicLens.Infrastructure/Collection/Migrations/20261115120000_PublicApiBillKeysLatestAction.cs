using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the column <c>latest_action_date</c> to <c>public_api.bill_keys</c>: the day of the latest official action that the active release pinned in <c>release_bill_statuses</c>.
/// The bill lists sort on it. It comes from the same status row as the stage, so it is null where the stage row is missing. The view shows the column last, so the view keeps its grants.
/// The rollback restores the view of <c>PublicApiBillKeys</c> with its owner and grant.
/// </summary>
public partial class PublicApiBillKeysLatestAction : Migration
{
    private const string UpSql = """
        CREATE OR REPLACE VIEW public_api.bill_keys WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.policy_area_id, b.event_date, s.status, s.latest_action_date
          FROM public_api.bills b
          LEFT JOIN public.release_bill_statuses s ON s.release_number = b.release_number AND s.record_id = b.record_id;
        """;

    private const string DownSql = """
        DROP VIEW public_api.bill_keys;

        CREATE VIEW public_api.bill_keys WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.policy_area_id, b.event_date, s.status
          FROM public_api.bills b
          LEFT JOIN public.release_bill_statuses s ON s.release_number = b.release_number AND s.record_id = b.record_id;

        ALTER VIEW public_api.bill_keys OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.bill_keys TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
