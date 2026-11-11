using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class ExecutiveActionDispositionTargets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "executive_action_revision_disposition_targets",
            columns: table => new
            {
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                relation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                number = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                reference_start = table.Column<int>(type: "integer", nullable: false),
                reference_length = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_executive_action_revision_disposition_targets", x => new { x.record_id, x.revision_number, x.position });
                table.CheckConstraint("ck_executive_action_revision_disposition_targets_kind", "kind IN ('ExecutiveOrder', 'Proclamation')");
                table.CheckConstraint("ck_executive_action_revision_disposition_targets_number", "number ~ '^[1-9][0-9]{0,5}$'");
                table.CheckConstraint("ck_executive_action_revision_disposition_targets_position", "position >= 0");
                table.CheckConstraint("ck_executive_action_revision_disposition_targets_relation", "relation IN ('Amends', 'Revokes')");
                table.CheckConstraint("ck_executive_action_revision_disposition_targets_span", "reference_start >= 0 AND reference_length > 0");
                table.ForeignKey(
                    name: "FK_executive_action_revision_disposition_targets_executive_act~",
                    columns: x => new { x.record_id, x.revision_number },
                    principalTable: "executive_action_revisions",
                    principalColumns: new[] { "record_id", "revision_number" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_executive_action_revision_disposition_targets_record_id_rev~",
            table: "executive_action_revision_disposition_targets",
            columns: new[] { "record_id", "revision_number", "relation", "kind", "number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "executive_action_revision_disposition_targets");
    }
}
