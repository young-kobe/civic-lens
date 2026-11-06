using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseBillStatuses : Migration
    {
        // The view drops and returns, because its column latest_action_text changes type, as in BillLineColumn.
        // bill_status shows the status that the release pinned, in release_bill_statuses. It never reads bill_gates, which holds the live status.
        // The bill must still show in public_api.bills, so the head of the view keeps the filters of that view.
        private const string UpBillStatusSql = """
            DROP VIEW public_api.bill_status;

            CREATE VIEW public_api.bill_status WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, s.latest_action_text, s.latest_action_date, s.read_at_utc_ticks
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.release_bill_statuses s ON s.release_number = a.release_number AND s.record_id = a.record_id
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.bill_status OWNER TO civic_lens_public_owner;
            """;

        private const string DownBillStatusSql = """
            DROP VIEW public_api.bill_status;

            CREATE VIEW public_api.bill_status WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, g.latest_action_text, g.latest_action_date, g.created_at_utc_ticks AS read_at_utc_ticks
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT x.latest_action_text, x.latest_action_date, x.created_at_utc_ticks
                     FROM public.bill_gates x
                    WHERE x.source_id = r.source_id AND x.congress = r.congress AND x.bill_type = r.bill_type AND x.bill_number = r.bill_number
                    ORDER BY x.update_date_utc_ticks DESC LIMIT 1) g
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND g.latest_action_text IS NOT NULL
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.bill_status OWNER TO civic_lens_public_owner;
            """;

        // record_headlines repeats the wording rule of PublishedRecordLabels. For an action or a vote, Wording gives ModelWording or CodeWording.
        // For a statement, StatementWording gives StatementModelWording, and only when code approved the revision. A person approval gives no label.
        // The latest Approve decision of the revision names the approver, as PublishedRecordRules.ApproverOf does. ReviewAuthor.CodeSubject names code.
        // The test PublicApiRecordPageViewsTests.RecordHeadlinesGiveTheLabelThatPublishedRecordLabelsGivesForEveryCase keeps the view and the C# rule equal.
        private const string OriginLabelSql = """
            CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END
            """;

        private const string UpRecordHeadlinesSql = $$"""
            CREATE OR REPLACE VIEW public_api.record_headlines WITH (security_barrier = true) AS
            SELECT a.kind, a.record_id, r.headline, r.text_origin, {{OriginLabelSql}} AS label
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL
            UNION ALL
            SELECT a.kind, a.record_id, r.headline, r.text_origin, {{OriginLabelSql}}
              FROM public_api.active_records a
              JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'vote' AND r.headline IS NOT NULL
            UNION ALL
            SELECT a.kind, a.record_id, r.headline, CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END,
                   CASE WHEN r.author_kind = 'Analysis' AND 'civic-lens:code' = (
                              SELECT d.actor_subject FROM public.statement_review_decisions d
                               WHERE d.candidate_id = r.candidate_id AND d.revision_number = r.revision_number AND d.kind = 'Approve'
                               ORDER BY d.review_state_version DESC LIMIT 1)
                        THEN 'An AI summary of this release.'
                   END
              FROM public_api.active_records a
              JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'statement' AND r.headline IS NOT NULL;
            """;

        private const string DownRecordHeadlinesSql = """
            CREATE OR REPLACE VIEW public_api.record_headlines WITH (security_barrier = true) AS
            SELECT x.kind, x.record_id, x.headline, x.text_origin,
                   CASE x.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label
              FROM (SELECT a.kind, a.record_id, r.headline, r.text_origin
                      FROM public_api.active_records a
                      JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
                     WHERE a.kind = 'action'
                    UNION ALL
                    SELECT a.kind, a.record_id, r.headline, r.text_origin
                      FROM public_api.active_records a
                      JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
                     WHERE a.kind = 'vote'
                    UNION ALL
                    SELECT a.kind, a.record_id, r.headline, CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END
                      FROM public_api.active_records a
                      JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
                     WHERE a.kind = 'statement') x
             WHERE x.headline IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_bill_statuses",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    latest_action_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    latest_action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    read_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_bill_statuses", x => new { x.release_number, x.record_id });
                    table.CheckConstraint("ck_release_bill_statuses_latest_action_text", "length(btrim(latest_action_text)) > 0 AND length(latest_action_text) <= 1000");
                    table.CheckConstraint("ck_release_bill_statuses_read_at_utc_ticks", "read_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_release_bill_statuses_record_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_release_bill_statuses_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(UpBillStatusSql);
            migrationBuilder.Sql(UpRecordHeadlinesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownBillStatusSql);
            migrationBuilder.Sql(DownRecordHeadlinesSql);
            migrationBuilder.DropTable(
                name: "release_bill_statuses");
        }
    }
}
