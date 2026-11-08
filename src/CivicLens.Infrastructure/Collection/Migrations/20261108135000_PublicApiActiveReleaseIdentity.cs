using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <inheritdoc />
public partial class PublicApiActiveReleaseIdentity : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.active_release WITH (security_barrier = true) AS
        SELECT release_number, published_at_utc_ticks
          FROM public.publication_releases
         WHERE is_active;

        ALTER VIEW public_api.active_release OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = "DROP VIEW public_api.active_release;";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
