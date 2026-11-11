using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiReleaseBillCounts : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.release_bill_counts WITH (security_barrier = true) AS
        SELECT c.release_number, c.congress, c.bills, c.expected_member_row_count
          FROM public.release_bill_counts c
          JOIN public.publication_releases r ON r.release_number = c.release_number
         WHERE r.is_active;

        CREATE VIEW public_api.release_official_bill_counts WITH (security_barrier = true) AS
        SELECT m.release_number, m.congress, m.official_id, m.sponsored, m.cosponsored
          FROM public.release_official_bill_counts m
          JOIN public.publication_releases r ON r.release_number = m.release_number
         WHERE r.is_active;

        ALTER VIEW public_api.release_bill_counts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_official_bill_counts OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP VIEW public_api.release_official_bill_counts, public_api.release_bill_counts;");
}
