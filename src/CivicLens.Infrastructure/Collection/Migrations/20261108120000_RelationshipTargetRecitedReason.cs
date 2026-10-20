using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261108120000_RelationshipTargetRecitedReason")]
public partial class RelationshipTargetRecitedReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_relationship_analysis_runs_reason", "relationship_analysis_runs");
        migrationBuilder.AddCheckConstraint("ck_relationship_analysis_runs_reason", "relationship_analysis_runs",
            "reason IS NULL OR reason IN ('unchanged','noRelationship','noLongerMatches','targetRecited')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE relationship_analysis_runs SET reason = 'unchanged' WHERE reason = 'targetRecited'");
        migrationBuilder.DropCheckConstraint("ck_relationship_analysis_runs_reason", "relationship_analysis_runs");
        migrationBuilder.AddCheckConstraint("ck_relationship_analysis_runs_reason", "relationship_analysis_runs",
            "reason IS NULL OR reason IN ('unchanged','noRelationship','noLongerMatches')");
    }
}
