using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicationWithdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "publication_withdrawals",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_revision_number = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    last_release = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_withdrawals", x => new { x.kind, x.record_id, x.first_release });
                    table.CheckConstraint("ck_publication_withdrawals_note", "char_length(note) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_publication_withdrawals_range", "last_release IS NULL OR last_release >= first_release");
                    table.CheckConstraint("ck_publication_withdrawals_revision", "withdrawn_revision_number >= 1");
                    table.CheckConstraint("ck_publication_withdrawals_withdrawn_at_utc_ticks", "withdrawn_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_publication_releases_first_release",
                        column: x => x.first_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_publication_releases_last_release",
                        column: x => x.last_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_published_records_kind_record_id",
                        columns: x => new { x.kind, x.record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_withdrawal_corrections",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    replaced_revision_number = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    facts_unchanged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_withdrawal_corrections", x => new { x.kind, x.record_id, x.first_release, x.position });
                    table.CheckConstraint("ck_publication_withdrawal_corrections_facts_unchanged", "note IS NULL OR NOT facts_unchanged");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_note", "note IS NULL OR char_length(note) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_position", "\"position\" >= 0");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_published_at_utc_ticks", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_revision", "replaced_revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_publication_withdrawal_corrections_publication_withdrawals_~",
                        columns: x => new { x.kind, x.record_id, x.first_release },
                        principalTable: "publication_withdrawals",
                        principalColumns: new[] { "kind", "record_id", "first_release" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publication_withdrawals_first_release",
                table: "publication_withdrawals",
                column: "first_release");

            migrationBuilder.CreateIndex(
                name: "IX_publication_withdrawals_last_release",
                table: "publication_withdrawals",
                column: "last_release");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_withdrawal_corrections");

            migrationBuilder.DropTable(
                name: "publication_withdrawals");
        }
    }
}
