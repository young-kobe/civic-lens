using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class VotePartyTallies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<short>(name: "party_tally_count", table: "vote_revisions", type: "smallint", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "ck_vote_revisions_party_tally_count", table: "vote_revisions", sql: "party_tally_count IS NULL OR party_tally_count BETWEEN 0 AND 16");
        migrationBuilder.CreateTable(
            name: "vote_revision_party_tallies",
            columns: table => new
            {
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                yea = table.Column<short>(type: "smallint", nullable: false),
                nay = table.Column<short>(type: "smallint", nullable: false),
                present = table.Column<short>(type: "smallint", nullable: false),
                not_voting = table.Column<short>(type: "smallint", nullable: false),
                other = table.Column<short>(type: "smallint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vote_revision_party_tallies", x => new { x.record_id, x.revision_number, x.party });
                table.ForeignKey("FK_vote_revision_party_tallies_vote_revisions_record_id_revisi~", x => new { x.record_id, x.revision_number }, "vote_revisions", new[] { "record_id", "revision_number" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("ck_vote_revision_party_tallies_party", "length(btrim(party)) > 0 AND length(party) <= 256");
                table.CheckConstraint("ck_vote_revision_party_tallies_buckets", "yea >= 0 AND nay >= 0 AND present >= 0 AND not_voting >= 0 AND other >= 0");
                table.CheckConstraint("ck_vote_revision_party_tallies_total", "yea + nay + present + not_voting + other BETWEEN 1 AND 600");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("vote_revision_party_tallies");
        migrationBuilder.DropCheckConstraint("ck_vote_revisions_party_tally_count", "vote_revisions");
        migrationBuilder.DropColumn("party_tally_count", "vote_revisions");
    }
}
