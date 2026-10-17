using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261031120000_VoteCandidateBillIndex")]
public partial class VoteCandidateBillIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_house_vote_candidates_bill",
            table: "house_vote_candidates",
            columns: new[] { "congress", "bill_type", "bill_number" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_house_vote_candidates_bill", table: "house_vote_candidates");
    }
}
