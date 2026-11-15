using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Lets code write a statement revision that holds the source text of a short release. The code author has a Bypassed run, like a code
/// action revision, and never a date. The two views that carry a statement label give such a revision the text origin Code and the label
/// <c>AiLabelWords.StatementSourceTextWording</c>, which SQL cannot read, so tests compare them. The Down fails while a code revision exists,
/// because the old checks refuse it. The run reason check adds <c>source-text</c>, the reason of the Bypassed run of such a revision.
/// </summary>
public partial class ShortStatementSourceText : Migration
{
    private const string SourceTextLabel = "The source text of this press release, with no AI.";

    private const string RunReasons =
        "reason IS NULL OR reason IN ('passed','passed-bill-or-vote','passed-bill-number','no-tracked-issue','over-byte-cap','gate-failed-open'";

    private const string TextOrigin = "CASE r.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Code' THEN 'Code' ELSE 'Person' END";

    private static readonly string Label = $"""
        CASE r.author_kind WHEN 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN '{PressReleaseStatementLabel.CodeLabel}' WHEN 'Person' THEN '{PressReleaseStatementLabel.PersonLabel}' END
                    WHEN 'Code' THEN '{SourceTextLabel}'
               END
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_statement_revisions_author_kind", table: "statement_revisions");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_revisions_run", table: "statement_revisions");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_revisions_author_kind", table: "statement_revisions",
            sql: "author_kind IN ('Person','Analysis','Code')");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_revisions_code_date", table: "statement_revisions",
            sql: "author_kind <> 'Code' OR statement_date IS NULL");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_revisions_run", table: "statement_revisions",
            sql: "(author_kind <> 'Person') = (run_id IS NOT NULL)");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_analysis_runs_reason", table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_analysis_runs_reason", table: "statement_analysis_runs", sql: RunReasons + ",'source-text')");
        migrationBuilder.Sql(StatementLabelViews);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(PressReleaseStatementLabel.StatementLabelViews(PressReleaseStatementLabel.CodeLabel));
        migrationBuilder.DropCheckConstraint(name: "ck_statement_revisions_author_kind", table: "statement_revisions");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_revisions_code_date", table: "statement_revisions");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_revisions_run", table: "statement_revisions");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_revisions_author_kind", table: "statement_revisions",
            sql: "author_kind IN ('Person','Analysis')");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_revisions_run", table: "statement_revisions",
            sql: "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_analysis_runs_reason", table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_analysis_runs_reason", table: "statement_analysis_runs", sql: RunReasons + ")");
    }

    private static readonly string StatementLabelViews = $$"""
        CREATE OR REPLACE VIEW public_api.statements WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.statement_date, r.headline, r.summary,
               {{TextOrigin}} AS text_origin,
               {{Label}} AS label,
               ARRAY(SELECT i.issue_id FROM public.statement_revision_issues i
                     WHERE i.candidate_id = r.candidate_id AND i.revision_number = r.revision_number ORDER BY i."position") AS issue_ids
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND r.summary IS NOT NULL AND e.approved_by IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.statement_revision_speakers s
                        WHERE s.candidate_id = r.candidate_id AND s.revision_number = r.revision_number);
        """ + RecordHeadlinesView(ChamberVoteCodeLabel.VoteCodeLabel("r", byChamber: false));

    /// <summary>The record headlines view with the statement label of this migration. The vote code label is a SQL expression, so a later
    /// migration can give it the chamber rule of <c>ChamberVoteCodeLabel</c>.</summary>
    internal static string RecordHeadlinesView(string voteCodeLabel) => $$"""
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
                 WHEN 'Code' THEN {{voteCodeLabel}}
               END
          FROM public_api.active_records a
          JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'vote' AND r.headline IS NOT NULL
        UNION ALL
        SELECT a.kind, a.record_id, r.headline, {{TextOrigin}}, {{Label}}
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND e.approved_by IS NOT NULL;
        """;
}
