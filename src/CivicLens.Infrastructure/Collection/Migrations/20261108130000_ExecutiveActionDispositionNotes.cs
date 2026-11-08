using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class ExecutiveActionDispositionNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "disposition_notes", table: "executive_action_revisions",
            type: "character varying(1999981)", maxLength: 1999981, nullable: true);
        migrationBuilder.AddColumn<string>(name: "disposition_notes_extraction_id", table: "executive_action_revisions",
            type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>(name: "disposition_notes_length", table: "executive_action_revisions", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "disposition_notes_start", table: "executive_action_revisions", type: "integer", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "ck_executive_action_revisions_disposition_notes", table: "executive_action_revisions",
            sql: "(disposition_notes_extraction_id IS NULL) = (disposition_notes_start IS NULL) AND (disposition_notes_extraction_id IS NULL) = (disposition_notes_length IS NULL) AND disposition_notes_start >= 0 AND disposition_notes_length > 0");
        migrationBuilder.AddCheckConstraint(name: "ck_executive_action_revisions_disposition_notes_written", table: "executive_action_revisions",
            sql: "(disposition_notes IS NULL) = (disposition_notes_extraction_id IS NULL)");
        migrationBuilder.CreateIndex(name: "IX_executive_action_revisions_disposition_notes_extraction_id", table: "executive_action_revisions",
            column: "disposition_notes_extraction_id");
        migrationBuilder.AddForeignKey(name: "FK_executive_action_revisions_document_extractions_disposition~", table: "executive_action_revisions",
            column: "disposition_notes_extraction_id", principalTable: "document_extractions", principalColumn: "extraction_id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_executive_action_revisions_document_extractions_disposition~", table: "executive_action_revisions");
        migrationBuilder.DropIndex(name: "IX_executive_action_revisions_disposition_notes_extraction_id", table: "executive_action_revisions");
        migrationBuilder.DropCheckConstraint(name: "ck_executive_action_revisions_disposition_notes", table: "executive_action_revisions");
        migrationBuilder.DropCheckConstraint(name: "ck_executive_action_revisions_disposition_notes_written", table: "executive_action_revisions");
        migrationBuilder.DropColumn(name: "disposition_notes", table: "executive_action_revisions");
        migrationBuilder.DropColumn(name: "disposition_notes_extraction_id", table: "executive_action_revisions");
        migrationBuilder.DropColumn(name: "disposition_notes_length", table: "executive_action_revisions");
        migrationBuilder.DropColumn(name: "disposition_notes_start", table: "executive_action_revisions");
    }
}
