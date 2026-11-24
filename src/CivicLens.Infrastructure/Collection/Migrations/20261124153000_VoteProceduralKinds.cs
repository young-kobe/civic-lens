using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261124153000_VoteProceduralKinds")]
public sealed class VoteProceduralKinds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_vote_revisions_kind", "vote_revisions");
        migrationBuilder.AddCheckConstraint("ck_vote_revisions_kind", "vote_revisions",
            "kind IN ('Rule', 'PreviousQuestion', 'MotionToRecommit', 'MotionToTable', 'MotionToDischarge', 'Amendment', 'Suspension', 'Passage', 'ConferenceReport', 'VetoOverride', 'Nomination', 'Cloture', 'MotionToProceed', 'Other')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_vote_revisions_kind", "vote_revisions");
        migrationBuilder.AddCheckConstraint("ck_vote_revisions_kind", "vote_revisions",
            "kind IN ('Rule', 'PreviousQuestion', 'MotionToRecommit', 'Amendment', 'Suspension', 'Passage', 'ConferenceReport', 'VetoOverride', 'Nomination', 'Cloture', 'MotionToProceed', 'Other')");
    }
}
