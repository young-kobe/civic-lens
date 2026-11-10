using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <inheritdoc />
public partial class PublicApiVoteFirstPublications : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.vote_first_publications WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, min(s.first_release) AS first_release
          FROM public_api.active_records a
          JOIN public.publication_record_states s ON s.kind = 'vote' AND s.record_id = a.record_id
                                                   AND s.first_release <= a.release_number
         WHERE a.kind = 'vote'
         GROUP BY a.record_id, a.revision_number, a.release_number;

        ALTER VIEW public_api.vote_first_publications OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = "DROP VIEW public_api.vote_first_publications;";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
