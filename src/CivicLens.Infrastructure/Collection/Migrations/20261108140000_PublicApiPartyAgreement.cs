using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <inheritdoc />
public partial class PublicApiPartyAgreement : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.party_agreement WITH (security_barrier = true) AS
        SELECT a.release_number, v.chamber, v.congress, v.kind,
               count(*) FILTER (WHERE v.alignment = 'PartiesAgree') AS agreed,
               count(*) FILTER (WHERE v.alignment = 'Split') AS split,
               count(*) FILTER (WHERE v.alignment = 'Mixed') AS mixed
          FROM public_api.active_records a
          LEFT JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
         WHERE a.kind = 'vote'
         GROUP BY a.release_number, v.chamber, v.congress, v.kind;

        ALTER VIEW public_api.party_agreement OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = "DROP VIEW public_api.party_agreement;";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
