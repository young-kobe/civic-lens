using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveActionRevisionColumns : Migration
    {
        private const string CheckSql = """
            DO $check$
            DECLARE mismatch text;
            BEGIN
                SELECT d.source_id || ' ' || d.document_number INTO mismatch FROM executive_action_documents d
                WHERE d.notice_json IS NOT NULL AND (d.document_number IS DISTINCT FROM d.notice_json::jsonb ->> 'documentNumber'
                    OR d.published_on IS DISTINCT FROM (d.notice_json::jsonb ->> 'publishedOn')::date)
                ORDER BY d.source_id, d.document_number LIMIT 1;
                IF mismatch IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot drop notice_json: the notice of document % differs from its row', mismatch;
                END IF;
                SELECT r.record_id || ' ' || r.revision_number INTO mismatch FROM executive_action_revisions r
                WHERE r.revision_json::jsonb ->> 'authorSubject' IS DISTINCT FROM 'civic-lens:code'
                ORDER BY r.record_id, r.revision_number LIMIT 1;
                IF mismatch IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot set the code author: revision % has another author', mismatch;
                END IF;
            END
            $check$
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_documents_pending",
                table: "executive_action_documents");

            migrationBuilder.AddColumn<string>(
                name: "author_kind",
                table: "executive_action_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Code");

            migrationBuilder.AddColumn<string>(
                name: "author_subject",
                table: "executive_action_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "civic-lens:code");

            migrationBuilder.AddColumn<string>(
                name: "document_number",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_url",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "federal_register_citation",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "executive_action_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "person_id",
                table: "executive_action_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "president_name",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "presidential_number",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "published_on",
                table: "executive_action_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "signed_on",
                table: "executive_action_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_id",
                table: "executive_action_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "executive_action_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_citation_extraction_id",
                table: "executive_action_documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notice_citation_length",
                table: "executive_action_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notice_citation_start",
                table: "executive_action_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_url",
                table: "executive_action_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql("ALTER TABLE executive_action_revisions ALTER COLUMN author_kind DROP DEFAULT, ALTER COLUMN author_subject DROP DEFAULT");
            migrationBuilder.Sql("LOCK TABLE executive_action_documents, executive_action_revisions IN SHARE ROW EXCLUSIVE MODE");
            migrationBuilder.Sql(CheckSql);
            migrationBuilder.Sql("""
                UPDATE executive_action_documents SET notice_url = notice_json::jsonb ->> 'url',
                    notice_citation_extraction_id = notice_json::jsonb -> 'citation' ->> 'extractionId',
                    notice_citation_start = (notice_json::jsonb -> 'citation' ->> 'start')::int,
                    notice_citation_length = (notice_json::jsonb -> 'citation' ->> 'length')::int
                WHERE notice_json IS NOT NULL
                """);

            migrationBuilder.DropColumn(
                name: "notice_json",
                table: "executive_action_documents");

            migrationBuilder.CreateTable(
                name: "executive_action_revision_facts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_facts", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_facts_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_executive_action_revision_facts_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_facts_document_extractions_citati~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revision_facts_executive_action_revisions_~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_revision_notices",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_notices", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_notices_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_notices_executive_action_document~",
                        columns: x => new { x.source_id, x.document_number },
                        principalTable: "executive_action_documents",
                        principalColumns: new[] { "source_id", "document_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revision_notices_executive_action_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions",
                sql: "author_kind = 'Code' AND author_subject = 'civic-lens:code'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_columns_written",
                table: "executive_action_revisions",
                sql: "num_nulls(source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url) IN (0, 10)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_dates",
                table: "executive_action_revisions",
                sql: "signed_on <> '0001-01-01' AND published_on <> '0001-01-01'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_fields",
                table: "executive_action_revisions",
                sql: "(document_number IS NULL OR (btrim(document_number) <> '' AND length(document_number) <= 1000 AND document_number !~ '[\\r\\n]')) AND (title IS NULL OR (btrim(title) <> '' AND length(title) <= 1000 AND title !~ '[\\r\\n]')) AND (federal_register_citation IS NULL OR (btrim(federal_register_citation) <> '' AND length(federal_register_citation) <= 1000 AND federal_register_citation !~ '[\\r\\n]')) AND (president_name IS NULL OR (btrim(president_name) <> '' AND length(president_name) <= 1000 AND president_name !~ '[\\r\\n]')) AND (document_url IS NULL OR (btrim(document_url) <> '' AND length(document_url) <= 1000 AND document_url !~ '[\\r\\n]')) AND (presidential_number IS NULL OR (btrim(presidential_number) <> '' AND length(presidential_number) <= 1000 AND presidential_number !~ '[\\r\\n]'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_ids",
                table: "executive_action_revisions",
                sql: "length(btrim(source_id)) > 0 AND length(btrim(person_id)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_kind",
                table: "executive_action_revisions",
                sql: "kind IN ('ExecutiveOrder', 'Proclamation', 'Notice', 'Memorandum')");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_documents_notice_citation_extraction_id",
                table: "executive_action_documents",
                column: "notice_citation_extraction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_documents_notice",
                table: "executive_action_documents",
                sql: "(notice_url IS NULL) = (notice_citation_extraction_id IS NULL) AND (notice_url IS NULL OR btrim(notice_url) <> '' AND length(notice_url) <= 1000)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_documents_notice_citation",
                table: "executive_action_documents",
                sql: "(notice_citation_extraction_id IS NULL) = (notice_citation_start IS NULL) AND (notice_citation_extraction_id IS NULL) = (notice_citation_length IS NULL) AND notice_citation_start >= 0 AND notice_citation_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_documents_pending",
                table: "executive_action_documents",
                sql: "revision_number IS NOT NULL OR notice_url IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_facts_citation_extraction_id",
                table: "executive_action_revision_facts",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_notices_record_id_revision_number~",
                table: "executive_action_revision_notices",
                columns: new[] { "record_id", "revision_number", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_notices_source_id_document_number",
                table: "executive_action_revision_notices",
                columns: new[] { "source_id", "document_number" });

            migrationBuilder.AddForeignKey(
                name: "FK_executive_action_documents_document_extractions_notice_cita~",
                table: "executive_action_documents",
                column: "notice_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_executive_action_documents_document_extractions_notice_cita~",
                table: "executive_action_documents");

            migrationBuilder.DropTable(
                name: "executive_action_revision_facts");

            migrationBuilder.DropTable(
                name: "executive_action_revision_notices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_columns_written",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_dates",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_fields",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_ids",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_kind",
                table: "executive_action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_executive_action_documents_notice_citation_extraction_id",
                table: "executive_action_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_documents_notice",
                table: "executive_action_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_documents_notice_citation",
                table: "executive_action_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_documents_pending",
                table: "executive_action_documents");

            migrationBuilder.DropColumn(
                name: "author_kind",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "author_subject",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "document_number",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "document_url",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "federal_register_citation",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "person_id",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "president_name",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "presidential_number",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "published_on",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "signed_on",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "title",
                table: "executive_action_revisions");

            migrationBuilder.AddColumn<string>(
                name: "notice_json",
                table: "executive_action_documents",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE executive_action_documents SET notice_json = jsonb_build_object('documentNumber', document_number,
                    'publishedOn', to_char(published_on, 'YYYY-MM-DD'), 'url', notice_url,
                    'citation', jsonb_build_object('extractionId', notice_citation_extraction_id, 'start', notice_citation_start,
                        'length', notice_citation_length))::text
                WHERE notice_url IS NOT NULL
                """);

            migrationBuilder.DropColumn(
                name: "notice_citation_extraction_id",
                table: "executive_action_documents");

            migrationBuilder.DropColumn(
                name: "notice_citation_length",
                table: "executive_action_documents");

            migrationBuilder.DropColumn(
                name: "notice_citation_start",
                table: "executive_action_documents");

            migrationBuilder.DropColumn(
                name: "notice_url",
                table: "executive_action_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_documents_pending",
                table: "executive_action_documents",
                sql: "revision_number IS NOT NULL OR notice_json IS NOT NULL");
        }
    }
}
