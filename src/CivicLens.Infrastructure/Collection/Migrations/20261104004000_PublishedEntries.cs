using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishedEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "published_entries",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    brief_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    brief_revision_number = table.Column<int>(type: "integer", nullable: true),
                    executive_action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    executive_action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    relationship_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    relationship_revision_number = table.Column<int>(type: "integer", nullable: true),
                    statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_revision_number = table.Column<int>(type: "integer", nullable: true),
                    document_change_draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    document_change_revision_number = table.Column<int>(type: "integer", nullable: true),
                    vote_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vote_revision_number = table.Column<int>(type: "integer", nullable: true),
                    headline = table.Column<string>(type: "text", nullable: false),
                    source_host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bill_title = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    link_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    statement_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, computedColumnSql: "CASE WHEN statement_record_id IS NULL THEN NULL ELSE 'statement' END", stored: true),
                    target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    target_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entries", x => new { x.kind, x.record_id, x.revision_number });
                    table.CheckConstraint("ck_published_entries_bill_title", "(kind = 'action') = (bill_title IS NOT NULL) AND (bill_title IS NULL OR length(btrim(bill_title)) > 0)");
                    table.CheckConstraint("ck_published_entries_document_type", "(kind = 'executiveAction') = (document_type IS NOT NULL) AND (document_type IS NULL OR document_type IN ('ExecutiveOrder', 'Proclamation', 'Notice', 'Memorandum'))");
                    table.CheckConstraint("ck_published_entries_headline", "length(btrim(headline)) > 0");
                    table.CheckConstraint("ck_published_entries_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_published_entries_link", "(kind = 'relationship') = (link_type IS NOT NULL) AND num_nonnulls(link_type, statement_record_id, target_kind, target_record_id) IN (0, 4) AND (link_type IS NULL OR (link_type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor', 'StatementAboutVote') AND target_kind IN ('action', 'vote') AND statement_record_id ~ '^[0-9a-f]{64}$' AND target_record_id ~ '^[0-9a-f]{64}$' AND (target_kind = 'vote') = (link_type = 'StatementAboutVote')))");
                    table.CheckConstraint("ck_published_entries_record_id", "record_id ~ CASE WHEN kind = 'documentChange' THEN '^[0-9a-f]{32}$' ELSE '^[0-9a-f]{64}$' END");
                    table.CheckConstraint("ck_published_entries_revision_key", "(kind = 'documentChange' AND document_change_draft_id IS NOT DISTINCT FROM record_id AND document_change_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'statement' AND statement_candidate_id IS NOT DISTINCT FROM record_id AND statement_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'action' AND action_record_id IS NOT DISTINCT FROM record_id AND action_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'brief' AND brief_record_id IS NOT DISTINCT FROM record_id AND brief_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'vote' AND vote_record_id IS NOT DISTINCT FROM record_id AND vote_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'relationship' AND relationship_record_id IS NOT DISTINCT FROM record_id AND relationship_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'executiveAction' AND executive_action_record_id IS NOT DISTINCT FROM record_id AND executive_action_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number) = 0)");
                    table.CheckConstraint("ck_published_entries_revision_number", "revision_number >= 1");
                    table.CheckConstraint("ck_published_entries_source_host", "source_host IS NULL OR length(btrim(source_host)) > 0");
                    table.ForeignKey(
                        name: "FK_published_entries_action_revisions_action_record_id_action_~",
                        columns: x => new { x.action_record_id, x.action_revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_brief_revisions_brief_record_id_brief_rev~",
                        columns: x => new { x.brief_record_id, x.brief_revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_document_change_revisions_document_change~",
                        columns: x => new { x.document_change_draft_id, x.document_change_revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_executive_action_revisions_executive_acti~",
                        columns: x => new { x.executive_action_record_id, x.executive_action_revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_kind_record_id",
                        columns: x => new { x.kind, x.record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_statement_kind_statemen~",
                        columns: x => new { x.statement_kind, x.statement_record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_target_kind_target_reco~",
                        columns: x => new { x.target_kind, x.target_record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_relationship_revisions_relationship_recor~",
                        columns: x => new { x.relationship_record_id, x.relationship_revision_number },
                        principalTable: "relationship_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_statement_revisions_statement_candidate_i~",
                        columns: x => new { x.statement_candidate_id, x.statement_revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_vote_revisions_vote_record_id_vote_revisi~",
                        columns: x => new { x.vote_record_id, x.vote_revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_issues",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_issues", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_issues_kind_record_id_revision_number_issue~", x => new { x.kind, x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_published_entry_issues_issue_id", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_published_entry_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_issues_published_entries_kind_record_id_rev~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_officials",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_officials", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_officials_kind_record_id_revision_number_of~", x => new { x.kind, x.record_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_published_entry_officials_matched_kind", "NOT is_matched OR kind = 'relationship'");
                    table.CheckConstraint("ck_published_entry_officials_official_id", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_published_entry_officials_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_officials_published_entries_kind_record_id_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_policy_areas",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_policy_areas", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_policy_areas_kind_record_id_revision_number~", x => new { x.kind, x.record_id, x.revision_number, x.policy_area_id });
                    table.CheckConstraint("ck_published_entry_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
                    table.CheckConstraint("ck_published_entry_policy_areas_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_policy_areas_published_entries_kind_record_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_action_record_id_action_revision_number",
                table: "published_entries",
                columns: new[] { "action_record_id", "action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_brief_record_id_brief_revision_number",
                table: "published_entries",
                columns: new[] { "brief_record_id", "brief_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_document_change_draft_id_document_change_~",
                table: "published_entries",
                columns: new[] { "document_change_draft_id", "document_change_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_executive_action_record_id_executive_acti~",
                table: "published_entries",
                columns: new[] { "executive_action_record_id", "executive_action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_relationship_record_id_relationship_revis~",
                table: "published_entries",
                columns: new[] { "relationship_record_id", "relationship_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_statement_candidate_id_statement_revision~",
                table: "published_entries",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_statement_kind_statement_record_id",
                table: "published_entries",
                columns: new[] { "statement_kind", "statement_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_target_kind_target_record_id",
                table: "published_entries",
                columns: new[] { "target_kind", "target_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_vote_record_id_vote_revision_number",
                table: "published_entries",
                columns: new[] { "vote_record_id", "vote_revision_number" });

            migrationBuilder.CreateIndex(
                name: "ux_published_entry_officials_matched",
                table: "published_entry_officials",
                columns: new[] { "kind", "record_id", "revision_number" },
                unique: true,
                filter: "is_matched");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "published_entry_issues");

            migrationBuilder.DropTable(
                name: "published_entry_officials");

            migrationBuilder.DropTable(
                name: "published_entry_policy_areas");

            migrationBuilder.DropTable(
                name: "published_entries");
        }
    }
}
