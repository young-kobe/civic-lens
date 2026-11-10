using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiVotePartyTallies : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.vote_party_tallies WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, v.party_tally_count,
               t.party, t.yea, t.nay, t.present, t.not_voting, t.other
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.vote_revision_party_tallies t
                 ON t.record_id = v.record_id AND t.revision_number = v.revision_number
         WHERE a.kind = 'vote';

        ALTER VIEW public_api.vote_party_tallies OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.vote_party_tallies;
        REVOKE SELECT (party_tally_count) ON vote_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, party, yea, nay, present, not_voting, other)
            ON vote_revision_party_tallies FROM civic_lens_public_owner;
        """);
}
