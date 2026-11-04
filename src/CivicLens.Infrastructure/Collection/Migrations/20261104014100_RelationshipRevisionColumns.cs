using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class RelationshipRevisionColumns : Migration
    {
        private const string CheckSql = """
            DO $check$
            DECLARE unreadable text;
            BEGIN
                SELECT r.record_id || ' ' || r.revision_number INTO unreadable FROM relationship_revisions r
                WHERE NOT pg_input_is_valid(r.revision_json, 'jsonb')
                ORDER BY r.record_id, r.revision_number LIMIT 1;
                IF unreadable IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot add the relationship revision columns: revision % is not valid JSON', unreadable;
                END IF;
            END
            $check$
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE relationship_revisions IN ACCESS EXCLUSIVE MODE");
            migrationBuilder.Sql(CheckSql);

            migrationBuilder.AddColumn<string>(
                name: "match_basis",
                table: "relationship_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "match_extraction_id",
                table: "relationship_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "match_length",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "match_start",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "official_id",
                table: "relationship_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "relationship_revisions",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statement_candidate_id",
                table: "relationship_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statement_quote_extraction_id",
                table: "relationship_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "statement_quote_length",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "statement_quote_start",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "statement_revision_number",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_action_record_id",
                table: "relationship_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_action_revision_number",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_fact_id",
                table: "relationship_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_kind",
                table: "relationship_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_vote_record_id",
                table: "relationship_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_vote_revision_number",
                table: "relationship_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "relationship_revisions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_match_extraction_id",
                table: "relationship_revisions",
                column: "match_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_statement_candidate_id_statement_rev~",
                table: "relationship_revisions",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_statement_quote_extraction_id",
                table: "relationship_revisions",
                column: "statement_quote_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_target_action_record_id_target_actio~",
                table: "relationship_revisions",
                columns: new[] { "target_action_record_id", "target_action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_target_vote_record_id_target_vote_re~",
                table: "relationship_revisions",
                columns: new[] { "target_vote_record_id", "target_vote_revision_number" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_columns_written",
                table: "relationship_revisions",
                sql: "num_nulls(statement_candidate_id, statement_revision_number, target_kind, match_basis, match_extraction_id, match_start, match_length, type, reason, statement_quote_extraction_id, statement_quote_start, statement_quote_length, target_fact_id, official_id) IN (0, 14)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_fact_id",
                table: "relationship_revisions",
                sql: "btrim(target_fact_id) <> '' AND length(target_fact_id) <= 16");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_match",
                table: "relationship_revisions",
                sql: "match_start >= 0 AND match_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_match_basis",
                table: "relationship_revisions",
                sql: "match_basis IN ('BillNumber', 'BillTitle')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_official_id",
                table: "relationship_revisions",
                sql: "btrim(official_id) <> '' AND length(official_id) <= 128");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_quote",
                table: "relationship_revisions",
                sql: "statement_quote_length <= 4096 AND statement_quote_extraction_id = match_extraction_id\nAND statement_quote_start <= match_start AND match_start + match_length <= statement_quote_start + statement_quote_length");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_reason",
                table: "relationship_revisions",
                sql: "btrim(reason) <> '' AND length(reason) <= 400 AND reason !~ '[\\r\\n]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_statement",
                table: "relationship_revisions",
                sql: "statement_candidate_id ~ '^[0-9a-f]{64}$' AND statement_revision_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_statement_quote",
                table: "relationship_revisions",
                sql: "statement_quote_start >= 0 AND statement_quote_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_target",
                table: "relationship_revisions",
                sql: "target_kind IS NULL AND num_nonnulls(target_action_record_id, target_action_revision_number, target_vote_record_id, target_vote_revision_number) = 0\nOR target_kind = 'Action' AND num_nonnulls(target_action_record_id, target_action_revision_number) = 2 AND num_nonnulls(target_vote_record_id, target_vote_revision_number) = 0\nOR target_kind = 'Vote' AND num_nonnulls(target_vote_record_id, target_vote_revision_number) = 2 AND num_nonnulls(target_action_record_id, target_action_revision_number) = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_target_ids",
                table: "relationship_revisions",
                sql: "target_action_record_id ~ '^[0-9a-f]{64}$' AND target_action_revision_number >= 1 AND target_vote_record_id ~ '^[0-9a-f]{64}$' AND target_vote_revision_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_type",
                table: "relationship_revisions",
                sql: "type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor', 'StatementAboutVote')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_type_target",
                table: "relationship_revisions",
                sql: "target_kind = 'Action' AND type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor') OR target_kind = 'Vote' AND type IN ('StatementAboutVote')");

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_action_revisions_target_action_recor~",
                table: "relationship_revisions",
                columns: new[] { "target_action_record_id", "target_action_revision_number" },
                principalTable: "action_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_document_extractions_match_extractio~",
                table: "relationship_revisions",
                column: "match_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_document_extractions_statement_quote~",
                table: "relationship_revisions",
                column: "statement_quote_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_statement_revisions_statement_candid~",
                table: "relationship_revisions",
                columns: new[] { "statement_candidate_id", "statement_revision_number" },
                principalTable: "statement_revisions",
                principalColumns: new[] { "candidate_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_vote_revisions_target_vote_record_id~",
                table: "relationship_revisions",
                columns: new[] { "target_vote_record_id", "target_vote_revision_number" },
                principalTable: "vote_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_relationship_revisions_action_revisions_target_action_recor~",
                table: "relationship_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_revisions_document_extractions_match_extractio~",
                table: "relationship_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_revisions_document_extractions_statement_quote~",
                table: "relationship_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_revisions_statement_revisions_statement_candid~",
                table: "relationship_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_revisions_vote_revisions_target_vote_record_id~",
                table: "relationship_revisions");

            migrationBuilder.DropIndex(
                name: "IX_relationship_revisions_match_extraction_id",
                table: "relationship_revisions");

            migrationBuilder.DropIndex(
                name: "IX_relationship_revisions_statement_candidate_id_statement_rev~",
                table: "relationship_revisions");

            migrationBuilder.DropIndex(
                name: "IX_relationship_revisions_statement_quote_extraction_id",
                table: "relationship_revisions");

            migrationBuilder.DropIndex(
                name: "IX_relationship_revisions_target_action_record_id_target_actio~",
                table: "relationship_revisions");

            migrationBuilder.DropIndex(
                name: "IX_relationship_revisions_target_vote_record_id_target_vote_re~",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_columns_written",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_fact_id",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_match",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_match_basis",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_official_id",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_quote",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_reason",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_statement",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_statement_quote",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_target",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_target_ids",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_type",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_type_target",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "match_basis",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "match_extraction_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "match_length",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "match_start",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "official_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "statement_candidate_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "statement_quote_extraction_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "statement_quote_length",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "statement_quote_start",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "statement_revision_number",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_action_record_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_action_revision_number",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_fact_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_kind",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_vote_record_id",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "target_vote_revision_number",
                table: "relationship_revisions");

            migrationBuilder.DropColumn(
                name: "type",
                table: "relationship_revisions");
        }
    }
}
