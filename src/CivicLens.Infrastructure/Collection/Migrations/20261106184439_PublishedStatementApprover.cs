using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishedStatementApprover : Migration
    {
        private const string UpHeadlinesSql = """
            CREATE OR REPLACE VIEW public_api.record_headlines WITH (security_barrier = true) AS
            SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL
            UNION ALL
            SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END
              FROM public_api.active_records a
              JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'vote' AND r.headline IS NOT NULL
            UNION ALL
            SELECT a.kind, a.record_id, r.headline, CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END,
                   CASE WHEN r.author_kind = 'Analysis' AND e.approved_by = 'Code'
                        THEN 'An AI summary of this release.'
                   END
              FROM public_api.active_records a
              JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
             WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND e.approved_by IS NOT NULL;
            REVOKE SELECT (candidate_id, revision_number, review_state_version, kind, actor_subject)
                ON public.statement_review_decisions FROM civic_lens_public_owner;
            """;

        private const string DownHeadlinesSql = """
            CREATE OR REPLACE VIEW public_api.record_headlines WITH (security_barrier = true) AS
            SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL
            UNION ALL
            SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END
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
            GRANT SELECT (candidate_id, revision_number, review_state_version, kind, actor_subject)
                ON public.statement_review_decisions TO civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                table: "published_entries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_entries_approved_by",
                table: "published_entries",
                sql: "approved_by IS NULL OR (kind = 'statement' AND approved_by IN ('Code', 'Person'))");

            migrationBuilder.Sql(UpHeadlinesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownHeadlinesSql);
            migrationBuilder.DropCheckConstraint(
                name: "ck_published_entries_approved_by",
                table: "published_entries");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "published_entries");
        }
    }
}
