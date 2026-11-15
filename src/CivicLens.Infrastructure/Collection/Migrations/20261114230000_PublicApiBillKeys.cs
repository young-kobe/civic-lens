using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds <c>public_api.bill_keys</c>: one narrow row for each bill of <c>public_api.bills</c>, with the columns that the bill lists filter and sort on and the stored status stage.
/// It reads through <c>public_api.bills</c>, so the eligibility of a bill keeps its one owner, and Postgres drops the wide bill joins that a key read does not use.
/// The stage comes from <c>release_bill_statuses</c> by release and record, as <c>public_api.bill_status</c> joins it. A filter on the stage is then a column test, not a join
/// to a view that Postgres underestimates. The rollback drops the view.
/// </summary>
public partial class PublicApiBillKeys : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.bill_keys WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.policy_area_id, b.event_date, s.status
          FROM public_api.bills b
          LEFT JOIN public.release_bill_statuses s ON s.release_number = b.release_number AND s.record_id = b.record_id;

        ALTER VIEW public_api.bill_keys OWNER TO civic_lens_public_owner;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP VIEW public_api.bill_keys;");
}
