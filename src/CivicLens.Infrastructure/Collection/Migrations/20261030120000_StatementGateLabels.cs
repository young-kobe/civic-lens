using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261030120000_StatementGateLabels")]
public partial class StatementGateLabels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "statement_gate_labels",
            columns: table => new
            {
                label_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                label_json = table.Column<string>(type: "text", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_gate_labels", x => x.label_id);
                table.CheckConstraint("ck_statement_gate_labels_id", "label_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_statement_gate_labels_author_kind", "author_kind IN ('Person','Analysis')");
                table.CheckConstraint("ck_statement_gate_labels_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_statement_gate_labels_statement_candidates_candidate_id",
                    column: x => x.candidate_id,
                    principalTable: "statement_candidates",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_statement_gate_labels_candidate_id_created_at_utc_ticks",
            table: "statement_gate_labels",
            columns: new[] { "candidate_id", "created_at_utc_ticks" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "statement_gate_labels");
    }
}
