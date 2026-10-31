using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReviewDecisionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "vote_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "vote_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "vote_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "statement_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "statement_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "statement_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "relationship_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "relationship_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "relationship_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "executive_action_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "executive_action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "executive_action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "document_change_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "document_change_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "document_change_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "brief_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "brief_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "brief_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_subject",
                table: "action_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                table: "action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_vote_review_decisions_decision_id_record_id",
                table: "vote_review_decisions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_statement_review_decisions_decision_id_candidate_id",
                table: "statement_review_decisions",
                columns: new[] { "decision_id", "candidate_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_relationship_review_decisions_decision_id_record_id",
                table: "relationship_review_decisions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_executive_action_review_decisions_decision_id_record_id",
                table: "executive_action_review_decisions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_change_review_decisions_decision_id_draft_id",
                table: "document_change_review_decisions",
                columns: new[] { "decision_id", "draft_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_brief_review_decisions_decision_id_record_id",
                table: "brief_review_decisions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_action_review_decisions_decision_id_record_id",
                table: "action_review_decisions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateTable(
                name: "action_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_action_review_decision_resolutions_action_review_decisions_~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_review_decision_resolutions_action_review_decisions~1",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_brief_review_decision_resolutions_brief_review_decisions_de~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "brief_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_review_decision_resolutions_brief_review_decisions_re~",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "brief_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_document_change_review_decision_resolutions_document_change~",
                        columns: x => new { x.decision_id, x.draft_id },
                        principalTable: "document_change_review_decisions",
                        principalColumns: new[] { "decision_id", "draft_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_review_decision_resolutions_document_chang~1",
                        columns: x => new { x.resolved_decision_id, x.draft_id },
                        principalTable: "document_change_review_decisions",
                        principalColumns: new[] { "decision_id", "draft_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_executive_action_review_decision_resolutions_executive_acti~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "executive_action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_review_decision_resolutions_executive_act~1",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "executive_action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relationship_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_relationship_review_decision_resolutions_relationship_revie~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "relationship_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relationship_review_decision_resolutions_relationship_revi~1",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "relationship_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_statement_review_decision_resolutions_statement_review_deci~",
                        columns: x => new { x.decision_id, x.candidate_id },
                        principalTable: "statement_review_decisions",
                        principalColumns: new[] { "decision_id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_review_decision_resolutions_statement_review_dec~1",
                        columns: x => new { x.resolved_decision_id, x.candidate_id },
                        principalTable: "statement_review_decisions",
                        principalColumns: new[] { "decision_id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_vote_review_decision_resolutions_vote_review_decisions_deci~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "vote_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_review_decision_resolutions_vote_review_decisions_reso~",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "vote_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decision_resolutions_decision_id_record_id",
                table: "action_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decision_resolutions_resolved_decision_id_rec~",
                table: "action_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decision_resolutions_decision_id_record_id",
                table: "brief_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decision_resolutions_resolved_decision_id_reco~",
                table: "brief_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decision_resolutions_decision_id_dra~",
                table: "document_change_review_decision_resolutions",
                columns: new[] { "decision_id", "draft_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decision_resolutions_resolved_decisi~",
                table: "document_change_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "draft_id" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decision_resolutions_decision_id_re~",
                table: "executive_action_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decision_resolutions_resolved_decis~",
                table: "executive_action_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_review_decision_resolutions_decision_id_record~",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_review_decision_resolutions_resolved_decision_~",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decision_resolutions_decision_id_candidate~",
                table: "statement_review_decision_resolutions",
                columns: new[] { "decision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decision_resolutions_resolved_decision_id_~",
                table: "statement_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decision_resolutions_decision_id_record_id",
                table: "vote_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decision_resolutions_resolved_decision_id_recor~",
                table: "vote_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            foreach (var (prefix, idColumn) in DecisionTables)
                migrationBuilder.Sql(BackfillSql(prefix, idColumn));

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "document_change_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "statement_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "action_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "vote_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "executive_action_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "relationship_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_subject",
                table: "brief_review_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }

        private static readonly (string Prefix, string IdColumn)[] DecisionTables =
        [
            ("document_change", "draft_id"), ("statement", "candidate_id"), ("action", "record_id"), ("vote", "record_id"),
            ("executive_action", "record_id"), ("relationship", "record_id"), ("brief", "record_id")
        ];

        // Ticks count from 0001-01-01 and a microsecond is 10 ticks. timestamptz keeps microseconds, so the check allows 1 microsecond of rounding.
        private const long DotNetEpochMicroseconds = 62_135_596_800_000_000;

        // JSON holds the kind as a number: 0 Approve, 1 RequestChanges, 2 WithdrawApproval.
        private static string BackfillSql(string prefix, string idColumn)
        {
            var decisions = $"{prefix}_review_decisions";
            var resolutions = $"{prefix}_review_decision_resolutions";
            return $$"""
                UPDATE {{decisions}} SET
                    actor_subject = decision_json::jsonb ->> 'actorSubject',
                    note = decision_json::jsonb ->> 'note',
                    correction_note = decision_json::jsonb ->> 'correctionNote';
                INSERT INTO {{resolutions}} (decision_id, {{idColumn}}, resolved_decision_id)
                SELECT d.decision_id, d.{{idColumn}}, resolved.value
                FROM {{decisions}} d, jsonb_array_elements_text(d.decision_json::jsonb -> 'resolvedDecisionIds') AS resolved(value);
                DO $check$
                DECLARE mismatch text;
                BEGIN
                    SELECT d.decision_id INTO mismatch FROM {{decisions}} d
                    WHERE d.actor_subject IS NULL
                        OR d.decision_id IS DISTINCT FROM d.decision_json::jsonb ->> 'decisionId'
                        OR d.{{idColumn}} IS DISTINCT FROM d.decision_json::jsonb ->> 'draftId'
                        OR d.revision_number IS DISTINCT FROM (d.decision_json::jsonb ->> 'revisionNumber')::int
                        OR d.review_state_version IS DISTINCT FROM (d.decision_json::jsonb ->> 'reviewStateVersion')::int
                        OR d.kind IS DISTINCT FROM CASE (d.decision_json::jsonb ->> 'kind')
                            WHEN '0' THEN 'Approve' WHEN '1' THEN 'RequestChanges' WHEN '2' THEN 'WithdrawApproval' END
                        OR d.actor_subject IS DISTINCT FROM d.decision_json::jsonb ->> 'actorSubject'
                        OR d.note IS DISTINCT FROM d.decision_json::jsonb ->> 'note'
                        OR d.correction_note IS DISTINCT FROM d.decision_json::jsonb ->> 'correctionNote'
                        OR coalesce(abs(d.created_at_utc_ticks / 10 - {{DotNetEpochMicroseconds}}
                            - round(extract(epoch FROM (d.decision_json::jsonb ->> 'createdAtUtc')::timestamptz)::numeric * 1000000)) > 1, true)
                        OR EXISTS (SELECT 1 FROM {{resolutions}} r
                            JOIN {{decisions}} target ON target.decision_id = r.resolved_decision_id
                            WHERE r.decision_id = d.decision_id AND (d.kind <> 'Approve' OR target.kind NOT IN ('RequestChanges', 'WithdrawApproval')))
                        OR (SELECT coalesce(array_agg(r.resolved_decision_id::text ORDER BY r.resolved_decision_id), '{}')
                            FROM {{resolutions}} r WHERE r.decision_id = d.decision_id)
                            IS DISTINCT FROM (SELECT coalesce(array_agg(j.value ORDER BY j.value), '{}')
                            FROM jsonb_array_elements_text(d.decision_json::jsonb -> 'resolvedDecisionIds') AS j(value))
                    ORDER BY d.decision_id LIMIT 1;
                    IF mismatch IS NOT NULL THEN
                        RAISE EXCEPTION 'Decision JSON and columns differ in {{decisions}} for decision %', mismatch;
                    END IF;
                END
                $check$
                """;
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "brief_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "document_change_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "executive_action_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "relationship_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "statement_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "vote_review_decision_resolutions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_vote_review_decisions_decision_id_record_id",
                table: "vote_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_statement_review_decisions_decision_id_candidate_id",
                table: "statement_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_relationship_review_decisions_decision_id_record_id",
                table: "relationship_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_executive_action_review_decisions_decision_id_record_id",
                table: "executive_action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_change_review_decisions_decision_id_draft_id",
                table: "document_change_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_brief_review_decisions_decision_id_record_id",
                table: "brief_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_action_review_decisions_decision_id_record_id",
                table: "action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "vote_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "vote_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "vote_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "statement_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "statement_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "statement_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "relationship_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "relationship_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "relationship_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "executive_action_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "executive_action_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "executive_action_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "document_change_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "document_change_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "document_change_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "brief_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "brief_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "brief_review_decisions");

            migrationBuilder.DropColumn(
                name: "actor_subject",
                table: "action_review_decisions");

            migrationBuilder.DropColumn(
                name: "correction_note",
                table: "action_review_decisions");

            migrationBuilder.DropColumn(
                name: "note",
                table: "action_review_decisions");
        }
    }
}
