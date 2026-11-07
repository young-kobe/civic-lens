using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicReadIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE INDEX ix_action_revisions_bill_identity
            ON public.action_revisions (congress, bill_number, bill_type, record_id, revision_number)
            WHERE headline IS NOT NULL AND event_on IS NOT NULL;
        CREATE INDEX ix_vote_member_list_entries_tallies
            ON public.vote_member_list_entries (list_id) INCLUDE (vote_cast);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX public.ix_vote_member_list_entries_tallies;
        DROP INDEX public.ix_action_revisions_bill_identity;
        """);
}
