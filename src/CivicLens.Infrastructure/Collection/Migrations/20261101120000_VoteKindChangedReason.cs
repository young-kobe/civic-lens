using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261101120000_VoteKindChangedReason")]
public partial class VoteKindChangedReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_vote_analysis_runs_reason", table: "vote_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_vote_analysis_runs_reason", table: "vote_analysis_runs",
            sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_vote_analysis_runs_reason", table: "vote_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_vote_analysis_runs_reason", table: "vote_analysis_runs",
            sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged')");
    }
}
