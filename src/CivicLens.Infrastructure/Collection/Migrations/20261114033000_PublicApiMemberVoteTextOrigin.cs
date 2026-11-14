using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the column <c>text_origin</c> to <c>public_api.member_votes</c>, so a shared vote row on the official page knows who wrote the headline.
/// The column comes last, so the view keeps every other column in place. The rollback drops the column, so it rebuilds the view and
/// <c>official_party_lines</c>, which reads it, from <see cref="PublicApiMemberVoteBreaksParty"/>.
/// </summary>
public partial class PublicApiMemberVoteTextOrigin : Migration
{
    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.member_votes WITH (security_barrier = true) AS
        {PublicApiMemberVoteBreaksParty.MemberVotesColumns},
        {PublicApiMemberVoteBreaksParty.BreaksPartyColumn},
               v.text_origin
        {PublicApiMemberVoteBreaksParty.MemberVotesFrom};
        """;

    private static readonly string DownSql = $"""
        DROP VIEW public_api.official_party_lines;
        DROP VIEW public_api.member_votes;

        CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
        {PublicApiMemberVoteBreaksParty.MemberVotesColumns},
        {PublicApiMemberVoteBreaksParty.BreaksPartyColumn}
        {PublicApiMemberVoteBreaksParty.MemberVotesFrom};

        {PublicApiMemberVoteBreaksParty.PartyLinesSql}

        ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_party_lines OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.member_votes, public_api.official_party_lines TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
