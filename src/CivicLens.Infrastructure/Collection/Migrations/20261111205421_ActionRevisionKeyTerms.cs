using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionKeyTerms : Migration
    {
        private const string ViewSql = """
            CREATE VIEW public_api.bill_key_terms WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, k."position", k.term, k.definition,
                   'Key terms chosen by an AI model. Each definition is an exact quote from the definitions section of the bill. Code checked the quotes, not the choice of terms.' AS label
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_key_terms k ON k.record_id = r.record_id AND k.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
               AND r.text_origin = 'Model' AND r.text_package_id IS NOT NULL;

            ALTER VIEW public_api.bill_key_terms OWNER TO civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_revision_key_terms",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    term = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_key_terms", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_key_terms_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_key_terms_position", "position BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_action_revision_key_terms_quote", "citation_length <= 300 AND length(btrim(definition)) > 0");
                    table.CheckConstraint("ck_action_revision_key_terms_term", "length(btrim(term)) > 0");
                    table.ForeignKey(
                        name: "FK_action_revision_key_terms_action_revisions_record_id_revisi~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_key_terms_document_extractions_citation_ext~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_revision_key_terms_citation_extraction_id",
                table: "action_revision_key_terms",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ux_action_revision_key_terms_term",
                table: "action_revision_key_terms",
                columns: new[] { "record_id", "revision_number", "term" },
                unique: true);

            // The view serves a term and its definition quote for the origin Model only, the origin that carries the AI label. A person edit of the
            // text changes the origin, and the terms then leave the view, as why_it_matters does in public_api.bills.
            migrationBuilder.Sql(ViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW public_api.bill_key_terms;");
            migrationBuilder.DropTable(
                name: "action_revision_key_terms");
        }
    }
}
