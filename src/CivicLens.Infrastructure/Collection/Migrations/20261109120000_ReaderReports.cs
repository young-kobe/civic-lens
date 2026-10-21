using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261109120000_ReaderReports")]
public partial class ReaderReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "reader_report_cursor",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                cursor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reader_report_cursor", x => x.id);
                table.CheckConstraint("ck_reader_report_cursor_id", "id = 1");
            });

        migrationBuilder.CreateTable(
            name: "reader_reports",
            columns: table => new
            {
                report_id = table.Column<Guid>(type: "uuid", nullable: false),
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                received_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                resolved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                resolved_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reader_reports", x => x.report_id);
                table.CheckConstraint("ck_reader_reports_received", "received_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_reader_reports_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
                table.CheckConstraint("ck_reader_reports_resolution",
                    "(status = 'Open') = (resolved_by IS NULL AND resolved_at_utc_ticks IS NULL) AND (status <> 'CorrectionNeeded' OR note IS NOT NULL)");
                table.CheckConstraint("ck_reader_reports_revision", "revision_number >= 1");
                table.CheckConstraint("ck_reader_reports_status", "status IN ('Open','Dismissed','CorrectionNeeded')");
                table.CheckConstraint("ck_reader_reports_version", "version >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "ix_reader_reports_record",
            table: "reader_reports",
            column: "record_id");

        migrationBuilder.CreateIndex(
            name: "ix_reader_reports_status_received",
            table: "reader_reports",
            columns: new[] { "status", "received_at_utc_ticks" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "reader_reports");

        migrationBuilder.DropTable(name: "reader_report_cursor");
    }
}
