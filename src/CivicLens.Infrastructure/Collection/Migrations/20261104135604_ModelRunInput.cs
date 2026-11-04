using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ModelRunInput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "vote_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "vote_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "vote_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "vote_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "vote_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "statement_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "statement_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "statement_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "statement_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "statement_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "relationship_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "relationship_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "relationship_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "relationship_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "relationship_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "document_change_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "document_change_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "document_change_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "document_change_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "document_change_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "brief_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "brief_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "brief_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "brief_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "brief_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "action_analysis_runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                table: "action_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_schema_sha256",
                table: "action_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_prompt_sha256",
                table: "action_analysis_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_message",
                table: "action_analysis_runs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "analysis_output_schemas",
                columns: table => new
                {
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_output_schemas", x => x.text_sha256);
                    table.CheckConstraint("ck_analysis_output_schemas_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_analysis_output_schemas_hash", "text_sha256 = encode(sha256(convert_to(text, 'UTF8')), 'hex')");
                    table.CheckConstraint("ck_analysis_output_schemas_id", "text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_analysis_output_schemas_text", "char_length(text) BETWEEN 1 AND 100000");
                });

            migrationBuilder.CreateTable(
                name: "analysis_system_prompts",
                columns: table => new
                {
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_system_prompts", x => x.text_sha256);
                    table.CheckConstraint("ck_analysis_system_prompts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_analysis_system_prompts_hash", "text_sha256 = encode(sha256(convert_to(text, 'UTF8')), 'hex')");
                    table.CheckConstraint("ck_analysis_system_prompts_id", "text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_analysis_system_prompts_text", "char_length(text) BETWEEN 1 AND 100000");
                });

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_output_schema_sha256",
                table: "vote_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_system_prompt_sha256",
                table: "vote_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_input",
                table: "vote_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_input_outcome",
                table: "vote_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_max_output_tokens",
                table: "vote_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_user_message",
                table: "vote_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_output_schema_sha256",
                table: "statement_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_system_prompt_sha256",
                table: "statement_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_input",
                table: "statement_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_input_outcome",
                table: "statement_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_max_output_tokens",
                table: "statement_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_user_message",
                table: "statement_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_output_schema_sha256",
                table: "relationship_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_system_prompt_sha256",
                table: "relationship_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_input",
                table: "relationship_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_input_outcome",
                table: "relationship_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_max_output_tokens",
                table: "relationship_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_user_message",
                table: "relationship_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_analysis_runs_output_schema_sha256",
                table: "document_change_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_analysis_runs_system_prompt_sha256",
                table: "document_change_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_input",
                table: "document_change_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_input_outcome",
                table: "document_change_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_max_output_tokens",
                table: "document_change_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_user_message",
                table: "document_change_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_output_schema_sha256",
                table: "brief_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_system_prompt_sha256",
                table: "brief_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_input",
                table: "brief_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_input_outcome",
                table: "brief_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_max_output_tokens",
                table: "brief_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_user_message",
                table: "brief_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_output_schema_sha256",
                table: "action_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_system_prompt_sha256",
                table: "action_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_input",
                table: "action_analysis_runs",
                sql: "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_input_outcome",
                table: "action_analysis_runs",
                sql: "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_max_output_tokens",
                table: "action_analysis_runs",
                sql: "max_output_tokens IS NULL OR max_output_tokens > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_user_message",
                table: "action_analysis_runs",
                sql: "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");

            migrationBuilder.AddForeignKey(
                name: "FK_action_analysis_runs_analysis_output_schemas_output_schema_~",
                table: "action_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_analysis_runs_analysis_system_prompts_system_prompt_~",
                table: "action_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_runs_analysis_output_schemas_output_schema_s~",
                table: "brief_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_runs_analysis_system_prompts_system_prompt_s~",
                table: "brief_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_analysis_runs_analysis_output_schemas_outpu~",
                table: "document_change_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_analysis_runs_analysis_system_prompts_syste~",
                table: "document_change_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_analysis_runs_analysis_output_schemas_output_s~",
                table: "relationship_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_analysis_runs_analysis_system_prompts_system_p~",
                table: "relationship_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_analysis_runs_analysis_output_schemas_output_sche~",
                table: "statement_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_analysis_runs_analysis_system_prompts_system_prom~",
                table: "statement_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_analysis_runs_analysis_output_schemas_output_schema_sh~",
                table: "vote_analysis_runs",
                column: "output_schema_sha256",
                principalTable: "analysis_output_schemas",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_analysis_runs_analysis_system_prompts_system_prompt_sh~",
                table: "vote_analysis_runs",
                column: "system_prompt_sha256",
                principalTable: "analysis_system_prompts",
                principalColumn: "text_sha256",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_analysis_runs_analysis_output_schemas_output_schema_~",
                table: "action_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_action_analysis_runs_analysis_system_prompts_system_prompt_~",
                table: "action_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_runs_analysis_output_schemas_output_schema_s~",
                table: "brief_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_runs_analysis_system_prompts_system_prompt_s~",
                table: "brief_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_runs_analysis_output_schemas_outpu~",
                table: "document_change_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_runs_analysis_system_prompts_syste~",
                table: "document_change_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_runs_analysis_output_schemas_output_s~",
                table: "relationship_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_runs_analysis_system_prompts_system_p~",
                table: "relationship_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_runs_analysis_output_schemas_output_sche~",
                table: "statement_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_runs_analysis_system_prompts_system_prom~",
                table: "statement_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_runs_analysis_output_schemas_output_schema_sh~",
                table: "vote_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_runs_analysis_system_prompts_system_prompt_sh~",
                table: "vote_analysis_runs");

            migrationBuilder.DropTable(
                name: "analysis_output_schemas");

            migrationBuilder.DropTable(
                name: "analysis_system_prompts");

            migrationBuilder.DropIndex(
                name: "IX_vote_analysis_runs_output_schema_sha256",
                table: "vote_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_vote_analysis_runs_system_prompt_sha256",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_input",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_input_outcome",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_max_output_tokens",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_user_message",
                table: "vote_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_statement_analysis_runs_output_schema_sha256",
                table: "statement_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_statement_analysis_runs_system_prompt_sha256",
                table: "statement_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_input",
                table: "statement_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_input_outcome",
                table: "statement_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_max_output_tokens",
                table: "statement_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_user_message",
                table: "statement_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_relationship_analysis_runs_output_schema_sha256",
                table: "relationship_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_relationship_analysis_runs_system_prompt_sha256",
                table: "relationship_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_input",
                table: "relationship_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_input_outcome",
                table: "relationship_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_max_output_tokens",
                table: "relationship_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_user_message",
                table: "relationship_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_document_change_analysis_runs_output_schema_sha256",
                table: "document_change_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_document_change_analysis_runs_system_prompt_sha256",
                table: "document_change_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_input",
                table: "document_change_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_input_outcome",
                table: "document_change_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_max_output_tokens",
                table: "document_change_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_user_message",
                table: "document_change_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_brief_analysis_runs_output_schema_sha256",
                table: "brief_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_brief_analysis_runs_system_prompt_sha256",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_input",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_input_outcome",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_max_output_tokens",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_user_message",
                table: "brief_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_action_analysis_runs_output_schema_sha256",
                table: "action_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_action_analysis_runs_system_prompt_sha256",
                table: "action_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_input",
                table: "action_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_input_outcome",
                table: "action_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_max_output_tokens",
                table: "action_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_user_message",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "statement_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "statement_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "statement_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "statement_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "statement_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "relationship_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "relationship_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "relationship_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "relationship_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "relationship_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "document_change_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "document_change_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "document_change_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "document_change_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "document_change_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "output_schema_sha256",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "system_prompt_sha256",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "user_message",
                table: "action_analysis_runs");
        }
    }
}
