using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class BillHistoryProjectionVersion : Migration
{
    private const string UpViewsSql = """
        CREATE OR REPLACE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, e.bill_line,
               CASE WHEN r.text_origin = 'Model' THEN r.why_it_matters END AS why_it_matters,
               t.version_type AS text_version_name, t.version_date AS text_version_date, t.chars_read AS text_chars_read, t.chars_total AS text_chars_total,
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count,
               r.crs_summary_text, CASE WHEN r.crs_summary_text IS NOT NULL THEN r.summary_version END AS crs_summary_version
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         CROSS JOIN LATERAL (
               SELECT count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                 FROM public.action_revision_sponsorships p
                WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
          LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          LEFT JOIN public.bill_text_versions t ON t.congress = r.congress AND t.bill_type = r.bill_type AND t.bill_number = r.bill_number
                                               AND t.package_id = r.text_package_id
          LEFT JOIN public.release_bill_histories rh ON rh.release_number = a.release_number AND rh.record_id = a.record_id
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id AND h.projection_version = rh.projection_version
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

        CREATE OR REPLACE VIEW public_api.bill_actions WITH (security_barrier = true) AS
        SELECT b.release_number, b.record_id, b.revision_number, a.position, a.action_date, a.action_time,
               a.text, a.type, a.action_code, a.source_system_code, a.source_system_name
          FROM public_api.bills b
          JOIN public.release_bill_histories r ON r.release_number = b.release_number AND r.record_id = b.record_id
          JOIN public.published_bill_history_actions a ON a.detail_job_id = r.detail_job_id AND a.projection_version = r.projection_version;
        ALTER VIEW public_api.bill_actions OWNER TO civic_lens_public_owner;
        """;

    private const string DownViewsSql = """
        CREATE OR REPLACE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, e.bill_line,
               CASE WHEN r.text_origin = 'Model' THEN r.why_it_matters END AS why_it_matters,
               t.version_type AS text_version_name, t.version_date AS text_version_date, t.chars_read AS text_chars_read, t.chars_total AS text_chars_total,
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count,
               r.crs_summary_text, CASE WHEN r.crs_summary_text IS NOT NULL THEN r.summary_version END AS crs_summary_version
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         CROSS JOIN LATERAL (
               SELECT count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                 FROM public.action_revision_sponsorships p
                WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
          LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          LEFT JOIN public.bill_text_versions t ON t.congress = r.congress AND t.bill_type = r.bill_type AND t.bill_number = r.bill_number
                                               AND t.package_id = r.text_package_id
          LEFT JOIN public.release_bill_histories rh ON rh.release_number = a.release_number AND rh.record_id = a.record_id
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

        CREATE OR REPLACE VIEW public_api.bill_actions WITH (security_barrier = true) AS
        SELECT b.release_number, b.record_id, b.revision_number, a.position, a.action_date, a.action_time,
               a.text, a.type, a.action_code, a.source_system_code, a.source_system_name
          FROM public_api.bills b
          JOIN public.release_bill_histories r ON r.release_number = b.release_number AND r.record_id = b.record_id
          JOIN public.published_bill_history_actions a ON a.detail_job_id = r.detail_job_id;
        ALTER VIEW public_api.bill_actions OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "projection_version", table: "published_bill_histories", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "projection_version", table: "published_bill_history_actions", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "projection_version", table: "release_bill_histories", type: "integer", nullable: false, defaultValue: 0);

        migrationBuilder.DropForeignKey(name: "FK_published_bill_history_actions_published_bill_histories_det~", table: "published_bill_history_actions");
        migrationBuilder.DropForeignKey(name: "FK_release_bill_histories_published_bill_histories_detail_job_~", table: "release_bill_histories");
        migrationBuilder.DropPrimaryKey(name: "PK_published_bill_history_actions", table: "published_bill_history_actions");
        migrationBuilder.DropPrimaryKey(name: "PK_published_bill_histories", table: "published_bill_histories");
        migrationBuilder.DropIndex(name: "IX_release_bill_histories_detail_job_id", table: "release_bill_histories");

        migrationBuilder.AddPrimaryKey(name: "PK_published_bill_histories", table: "published_bill_histories", columns: new[] { "detail_job_id", "projection_version" });
        migrationBuilder.AddPrimaryKey(name: "PK_published_bill_history_actions", table: "published_bill_history_actions", columns: new[] { "detail_job_id", "projection_version", "position" });
        migrationBuilder.AddCheckConstraint(name: "ck_published_bill_histories_projection_version", table: "published_bill_histories", sql: "projection_version >= 0");
        migrationBuilder.AddCheckConstraint(name: "ck_published_bill_history_actions_projection_version", table: "published_bill_history_actions", sql: "projection_version >= 0");
        migrationBuilder.AddCheckConstraint(name: "ck_release_bill_histories_projection_version", table: "release_bill_histories", sql: "projection_version >= 0");

        migrationBuilder.AddForeignKey(name: "FK_published_bill_history_actions_published_bill_histories_det~", table: "published_bill_history_actions", columns: new[] { "detail_job_id", "projection_version" }, principalTable: "published_bill_histories", principalColumns: new[] { "detail_job_id", "projection_version" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_release_bill_histories_published_bill_histories_detail_job_~", table: "release_bill_histories", columns: new[] { "detail_job_id", "projection_version" }, principalTable: "published_bill_histories", principalColumns: new[] { "detail_job_id", "projection_version" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(name: "IX_release_bill_histories_detail_job_id_projection_version", table: "release_bill_histories", columns: new[] { "detail_job_id", "projection_version" });

        migrationBuilder.Sql(UpViewsSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("LOCK TABLE published_bill_history_actions IN ACCESS EXCLUSIVE MODE");
        migrationBuilder.Sql("LOCK TABLE release_bill_histories IN ACCESS EXCLUSIVE MODE");
        migrationBuilder.Sql("LOCK TABLE published_bill_histories IN ACCESS EXCLUSIVE MODE");
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM published_bill_histories WHERE projection_version <> 0)
                   OR EXISTS (SELECT 1 FROM published_bill_history_actions WHERE projection_version <> 0)
                   OR EXISTS (SELECT 1 FROM release_bill_histories WHERE projection_version <> 0) THEN
                    RAISE EXCEPTION 'Cannot roll back bill history projection versions while nonzero versions exist';
                END IF;
            END $$;
            """);
        migrationBuilder.Sql(DownViewsSql);

        migrationBuilder.DropForeignKey(name: "FK_published_bill_history_actions_published_bill_histories_det~", table: "published_bill_history_actions");
        migrationBuilder.DropForeignKey(name: "FK_release_bill_histories_published_bill_histories_detail_job_~", table: "release_bill_histories");
        migrationBuilder.DropPrimaryKey(name: "PK_published_bill_history_actions", table: "published_bill_history_actions");
        migrationBuilder.DropPrimaryKey(name: "PK_published_bill_histories", table: "published_bill_histories");
        migrationBuilder.DropIndex(name: "IX_release_bill_histories_detail_job_id_projection_version", table: "release_bill_histories");
        migrationBuilder.DropCheckConstraint(name: "ck_published_bill_histories_projection_version", table: "published_bill_histories");
        migrationBuilder.DropCheckConstraint(name: "ck_published_bill_history_actions_projection_version", table: "published_bill_history_actions");
        migrationBuilder.DropCheckConstraint(name: "ck_release_bill_histories_projection_version", table: "release_bill_histories");

        migrationBuilder.AddPrimaryKey(name: "PK_published_bill_histories", table: "published_bill_histories", column: "detail_job_id");
        migrationBuilder.AddPrimaryKey(name: "PK_published_bill_history_actions", table: "published_bill_history_actions", columns: new[] { "detail_job_id", "position" });
        migrationBuilder.CreateIndex(name: "IX_release_bill_histories_detail_job_id", table: "release_bill_histories", column: "detail_job_id");
        migrationBuilder.AddForeignKey(name: "FK_published_bill_history_actions_published_bill_histories_det~", table: "published_bill_history_actions", column: "detail_job_id", principalTable: "published_bill_histories", principalColumn: "detail_job_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_release_bill_histories_published_bill_histories_detail_job_~", table: "release_bill_histories", column: "detail_job_id", principalTable: "published_bill_histories", principalColumn: "detail_job_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropColumn(name: "projection_version", table: "release_bill_histories");
        migrationBuilder.DropColumn(name: "projection_version", table: "published_bill_history_actions");
        migrationBuilder.DropColumn(name: "projection_version", table: "published_bill_histories");
    }
}
