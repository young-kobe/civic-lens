using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Changes the label of a statement summary that a model wrote and code approved to "An AI summary of this press release.". The two views that carry a
/// statement label repeat <c>AiLabelWords.StatementModelWording</c> and <c>AiLabelWords.StatementCheckedByPersonWording</c>, because SQL cannot read the
/// class. Tests compare them. Only the label of a code-approved statement changes.
/// </summary>
public partial class PressReleaseStatementLabel : Migration
{
    private const string CodeLabel = "An AI summary of this press release.";

    private const string FormerCodeLabel = "An AI summary of this release.";

    private const string PersonLabel = "Drafted by an AI model from this source. A person checked it before publication.";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(StatementLabelViews(CodeLabel));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(StatementLabelViews(FormerCodeLabel));

    private static string StatementLabelViews(string codeLabel) => $$"""
        CREATE OR REPLACE VIEW public_api.statements WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.statement_date, r.headline, r.summary,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN '{{codeLabel}}' WHEN 'Person' THEN '{{PersonLabel}}' END
               END AS label,
               ARRAY(SELECT i.issue_id FROM public.statement_revision_issues i
                     WHERE i.candidate_id = r.candidate_id AND i.revision_number = r.revision_number ORDER BY i."position") AS issue_ids
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND r.summary IS NOT NULL AND e.approved_by IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.statement_revision_speakers s
                        WHERE s.candidate_id = r.candidate_id AND s.revision_number = r.revision_number);

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
               CASE WHEN r.author_kind = 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN '{{codeLabel}}' WHEN 'Person' THEN '{{PersonLabel}}' END
               END
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND e.approved_by IS NOT NULL;
        """;
}
