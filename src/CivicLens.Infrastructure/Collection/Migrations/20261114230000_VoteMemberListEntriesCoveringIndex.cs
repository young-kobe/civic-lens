using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Replaces <c>ix_vote_member_list_entries_tallies</c> with a covering index on the list ID that holds every entry column a list read of
/// <c>public_api.member_votes</c> and <c>public_api.official_issue_counts</c> needs. So the board statement over
/// <c>public_api.official_vote_parties</c> reads the entries with an index-only scan and no heap page for each row.
/// The new index is built before the old one is dropped, so no read loses its list index.
/// </summary>
public partial class VoteMemberListEntriesCoveringIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE INDEX ix_vote_member_list_entries_members
            ON public.vote_member_list_entries (list_id) INCLUDE (person_id, party, vote_cast);
        DROP INDEX public.ix_vote_member_list_entries_tallies;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE INDEX ix_vote_member_list_entries_tallies
            ON public.vote_member_list_entries (list_id) INCLUDE (vote_cast);
        DROP INDEX public.ix_vote_member_list_entries_members;
        """);
}
