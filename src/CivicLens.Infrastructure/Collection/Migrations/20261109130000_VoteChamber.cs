using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261109130000_VoteChamber")]
public partial class VoteChamber : Migration
{
    private static readonly string[] VersionColumns = ["source_id", "congress", "session_number", "roll_call_number", "update_date_utc_ticks"];
    private static readonly string[] VersionChamberColumns = ["source_id", "chamber", "congress", "session_number", "roll_call_number", "update_date_utc_ticks"];
    private static readonly string[] DraftIndexColumns = ["source_id", "congress", "session_number", "roll_call_number"];
    private static readonly string[] DraftChamberIndexColumns = ["source_id", "chamber", "congress", "session_number", "roll_call_number"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_house_vote_candidates_roll", table: "house_vote_candidates");
        migrationBuilder.DropIndex(name: "ux_house_votes_version", table: "house_votes");
        migrationBuilder.DropIndex(name: "ux_vote_drafts_roll", table: "vote_drafts");

        migrationBuilder.RenameTable(name: "house_votes", newName: "vote_versions");
        migrationBuilder.RenameTable(name: "house_vote_candidates", newName: "vote_candidates");
        RenameVoteObjects(migrationBuilder, "house_votes", "vote_versions", "house_vote_candidates", "vote_candidates");

        foreach (var table in new[] { "vote_versions", "vote_candidates", "vote_drafts" })
        {
            migrationBuilder.AddColumn<string>(name: "chamber", table: table, type: "character varying(16)", maxLength: 16, nullable: false,
                defaultValue: "House");
            migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN chamber DROP DEFAULT");
            migrationBuilder.AddCheckConstraint($"ck_{table}_chamber", table, "chamber IN ('House', 'Senate')");
        }

        migrationBuilder.CreateIndex(name: "ix_vote_candidates_roll", table: "vote_candidates", columns: VersionChamberColumns);
        migrationBuilder.CreateIndex(name: "ux_vote_versions_roll", table: "vote_versions", columns: VersionChamberColumns, unique: true);
        migrationBuilder.CreateIndex(name: "ux_vote_drafts_roll", table: "vote_drafts", columns: DraftChamberIndexColumns, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DeleteSenateVotes(migrationBuilder);
        migrationBuilder.DropIndex(name: "ix_vote_candidates_roll", table: "vote_candidates");
        migrationBuilder.DropIndex(name: "ux_vote_versions_roll", table: "vote_versions");
        migrationBuilder.DropIndex(name: "ux_vote_drafts_roll", table: "vote_drafts");

        foreach (var table in new[] { "vote_versions", "vote_candidates", "vote_drafts" })
        {
            migrationBuilder.DropCheckConstraint($"ck_{table}_chamber", table);
            migrationBuilder.DropColumn(name: "chamber", table: table);
        }

        migrationBuilder.RenameTable(name: "vote_versions", newName: "house_votes");
        migrationBuilder.RenameTable(name: "vote_candidates", newName: "house_vote_candidates");
        RenameVoteObjects(migrationBuilder, "vote_versions", "house_votes", "vote_candidates", "house_vote_candidates");

        migrationBuilder.CreateIndex(name: "ix_house_vote_candidates_roll", table: "house_vote_candidates", columns: VersionColumns);
        migrationBuilder.CreateIndex(name: "ux_house_votes_version", table: "house_votes", columns: VersionColumns, unique: true);
        migrationBuilder.CreateIndex(name: "ux_vote_drafts_roll", table: "vote_drafts", columns: DraftIndexColumns, unique: true);
    }

    // The House-only tables cannot hold a Senate row, so a revert deletes the Senate rows first, children before parents.
    private static void DeleteSenateVotes(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM vote_review_decisions WHERE record_id IN (SELECT record_id FROM vote_drafts WHERE chamber = 'Senate');
        DELETE FROM vote_revisions WHERE record_id IN (SELECT record_id FROM vote_drafts WHERE chamber = 'Senate');
        DELETE FROM vote_drafts WHERE chamber = 'Senate';
        UPDATE vote_analysis_runs SET previous_run_id = NULL WHERE candidate_id IN (SELECT candidate_id FROM vote_candidates WHERE chamber = 'Senate');
        DELETE FROM vote_analysis_runs WHERE candidate_id IN (SELECT candidate_id FROM vote_candidates WHERE chamber = 'Senate');
        DELETE FROM vote_analysis WHERE candidate_id IN (SELECT candidate_id FROM vote_candidates WHERE chamber = 'Senate');
        DELETE FROM vote_candidates WHERE chamber = 'Senate';
        DELETE FROM vote_versions WHERE chamber = 'Senate';
        """);

    // RenameTable keeps the old constraint and index names, so this runs after it and renames them to match.
    private static void RenameVoteObjects(MigrationBuilder migrationBuilder, string versionsFrom, string versionsTo,
        string candidatesFrom, string candidatesTo)
    {
        RenameConstraints(migrationBuilder, versionsTo, versionsFrom, versionsTo,
            "PK_{0}", "FK_{0}_collection_jobs_detail_job_id", "FK_{0}_document_extractions_extraction_id",
            "ck_{0}_bill", "ck_{0}_roll", "ck_{0}_update");
        RenameConstraints(migrationBuilder, candidatesTo, candidatesFrom, candidatesTo,
            "PK_{0}", "FK_{0}_collection_jobs_members_job_id", "FK_{0}_document_extractions_detail_extraction_id",
            "FK_{0}_document_extractions_members_extraction_id", "fk_{0}_configuration_revision",
            "ck_{0}_bill", "ck_{0}_created", "ck_{0}_id", "ck_{0}_roll", "ck_{0}_update");
        migrationBuilder.Sql($"""
            ALTER TABLE {candidatesTo} RENAME CONSTRAINT "FK_{candidatesFrom}_{versionsFrom}_detail_job_id" TO "FK_{candidatesTo}_{versionsTo}_detail_job_id";
            ALTER TABLE vote_analysis RENAME CONSTRAINT "FK_vote_analysis_{candidatesFrom}_candidate_id" TO "FK_vote_analysis_{candidatesTo}_candidate_id";
            """);
        RenameIndexes(migrationBuilder, versionsTo, versionsFrom, versionsTo, "IX_{0}_extraction_id");
        RenameIndexes(migrationBuilder, candidatesTo, candidatesFrom, candidatesTo, "IX_{0}_configuration_revision_id",
            "IX_{0}_detail_extraction_id", "IX_{0}_members_extraction_id", "IX_{0}_members_job_id", "ux_{0}_detail_job", "ix_{0}_bill");
    }

    private static void RenameConstraints(MigrationBuilder migrationBuilder, string table, string from, string to, params string[] patterns)
    {
        foreach (var pattern in patterns)
            migrationBuilder.Sql($"ALTER TABLE {table} RENAME CONSTRAINT \"{string.Format(pattern, from)}\" TO \"{string.Format(pattern, to)}\"");
    }

    private static void RenameIndexes(MigrationBuilder migrationBuilder, string table, string from, string to, params string[] patterns)
    {
        foreach (var pattern in patterns)
            migrationBuilder.RenameIndex(name: string.Format(pattern, from), table: table, newName: string.Format(pattern, to));
    }
}
