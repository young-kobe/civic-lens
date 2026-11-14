using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds <c>action_revisions.introduced_on</c>, the introduced date of the Congress.gov bill record that the stored detail page of the candidate holds.
/// C# writes it with each revision, and <c>actions revisions backfill</c> fills it for older revisions. The view <c>public_api.bills</c> gets the column
/// at its end. It and the seven other views that leave out a revision with no event date now also leave out a revision with no introduced date, so no
/// child view shows a bill that <c>public_api.bills</c> hides. The rollback restores the seven views, drops the views that read the bill view, and restores
/// them and the bill view from <see cref="PublicApiBillSponsorPartiesAndTimes"/>.
/// </summary>
public partial class ActionRevisionIntroducedOn : Migration
{
    private const string EventGuard = "r.event_on IS NOT NULL";

    /// <summary>The seven views other than <c>public_api.bills</c> that hold <see cref="EventGuard"/>, as the previous migrations left them. Each holds it once.</summary>
    private const string GuardedViews = """
        CREATE OR REPLACE VIEW public_api.bill_sponsorships WITH (security_barrier = true) AS
         SELECT a.record_id,
            a.revision_number,
            p."position",
            p.person_id AS official_id,
            p.role,
            p.is_original,
            p.sponsored_on,
            p.withdrawn_on
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_sponsorships p ON p.record_id::text = r.record_id::text AND p.revision_number = r.revision_number
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL;
        CREATE OR REPLACE VIEW public_api.bill_issues WITH (security_barrier = true) AS
         SELECT a.record_id,
            a.revision_number,
            i.issue_id,
            i."position",
            r.congress
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_issues i ON i.record_id::text = r.record_id::text AND i.revision_number = r.revision_number
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked AND (EXISTS ( SELECT 1
                   FROM public.action_revision_sponsorships p
                  WHERE p.record_id::text = r.record_id::text AND p.revision_number = r.revision_number));
        CREATE OR REPLACE VIEW public_api.bill_key_terms WITH (security_barrier = true) AS
         SELECT a.record_id,
            a.revision_number,
            k."position",
            k.term,
            k.definition,
            k.cut,
            'Key terms chosen by an AI model. Each definition is an exact quote from the definitions section of the bill. Code checked the quotes, not the choice of terms.'::text AS label,
            regexp_replace(substr(regexp_replace("left"(d.text, k.citation_start + k.citation_length), '([\U00010000-\U0010FFFF])'::text, '\1\1'::text, 'g'::text), k.citation_start + 1, k.citation_length), '([\U00010000-\U0010FFFF])\1'::text, '\1'::text, 'g'::text) AS quote
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_key_terms k ON k.record_id::text = r.record_id::text AND k.revision_number = r.revision_number
             JOIN public.document_extractions d ON d.extraction_id::text = k.citation_extraction_id::text
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND r.text_origin::text = 'Model'::text AND r.text_package_id IS NOT NULL;
        CREATE OR REPLACE VIEW public_api.bill_text_sentences WITH (security_barrier = true) AS
         SELECT a.record_id,
            a.revision_number,
            x."position",
            x.part,
            x.sentence,
            regexp_replace(substr(regexp_replace("left"(d.text, x.citation_start + x.citation_length), '([\U00010000-\U0010FFFF])'::text, '\1\1'::text, 'g'::text), x.citation_start + 1, x.citation_length), '([\U00010000-\U0010FFFF])\1'::text, '\1'::text, 'g'::text) AS quote
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_text_citations x ON x.record_id::text = r.record_id::text AND x.revision_number = r.revision_number
             JOIN public.document_extractions d ON d.extraction_id::text = x.citation_extraction_id::text
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND r.text_origin::text = 'Model'::text AND r.text_package_id IS NOT NULL;
        CREATE OR REPLACE VIEW public_api.bill_status WITH (security_barrier = true) AS
         SELECT a.record_id,
            a.revision_number,
            s.latest_action_text,
            s.latest_action_date,
            s.read_at_utc_ticks,
            s.status
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.release_bill_statuses s ON s.release_number = a.release_number AND s.record_id::text = a.record_id::text
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND (EXISTS ( SELECT 1
                   FROM public.action_revision_sponsorships p
                  WHERE p.record_id::text = r.record_id::text AND p.revision_number = r.revision_number));
        CREATE OR REPLACE VIEW public_api.issue_sponsors WITH (security_barrier = true) AS
         SELECT i.issue_id,
            p.person_id AS official_id,
            r.event_on AS event_date
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_issues i ON i.record_id::text = r.record_id::text AND i.revision_number = r.revision_number
             JOIN public.action_revision_sponsorships p ON p.record_id::text = r.record_id::text AND p.revision_number = r.revision_number
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked AND p.role::text = 'Sponsor'::text;
        CREATE OR REPLACE VIEW public_api.official_issue_counts WITH (security_barrier = true) AS
         SELECT p.person_id AS official_id,
            r.congress,
            i.issue_id,
            'bill'::text AS source,
            NULL::text AS vote_kind,
            count(DISTINCT p.record_id) AS records
           FROM public_api.active_records a
             JOIN public.action_revisions r ON r.record_id::text = a.record_id::text AND r.revision_number = a.revision_number
             JOIN public.action_revision_sponsorships p ON p.record_id::text = r.record_id::text AND p.revision_number = r.revision_number AND p.role::text = 'Sponsor'::text
             JOIN public.action_revision_issues i ON i.record_id::text = r.record_id::text AND i.revision_number = r.revision_number AND i.is_tracked
          WHERE a.kind::text = 'action'::text AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
          GROUP BY p.person_id, r.congress, i.issue_id
        UNION ALL
         SELECT e.person_id AS official_id,
            v.congress,
            i.issue_id,
            'vote'::text AS source,
            v.kind::text AS vote_kind,
            count(*) AS records
           FROM public_api.active_records a
             JOIN public.vote_revisions v ON v.record_id::text = a.record_id::text AND v.revision_number = a.revision_number
             JOIN public.vote_member_list_entries e ON e.list_id::text = v.member_list_id::text AND e.vote_cast::text <> 'Not Voting'::text
             JOIN public.vote_revision_issues i ON i.record_id::text = v.record_id::text AND i.revision_number = v.revision_number AND i.is_tracked
          WHERE a.kind::text = 'vote'::text
          GROUP BY e.person_id, v.congress, i.issue_id, v.kind;
        """;

    private static readonly string GuardedViewsWithIntroducedOn = GuardedViews.Replace(EventGuard, EventGuard + " AND r.introduced_on IS NOT NULL", StringComparison.Ordinal);

    private const string UpSql = """
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
               r.crs_summary_text, CASE WHEN r.crs_summary_text IS NOT NULL THEN r.summary_version END AS crs_summary_version,
               r.text_origin = 'Model' AND r.text_package_id IS NULL AND r.summary_version IS NOT NULL AS summary_from_crs,
               r.introduced_on
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
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND r.introduced_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
        """;

    private static readonly string DownSql = """
        DROP VIEW public_api.bill_actions, public_api.votes, public_api.action_sources;
        DROP VIEW public_api.bills;
        """ + PublicApiBillSponsorPartiesAndTimes.BillsView + PublicApiBillSponsorPartiesAndTimes.BillDependentViews + """

        GRANT SELECT ON public_api.bills, public_api.bill_actions, public_api.votes, public_api.action_sources TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "introduced_on", table: "action_revisions", type: "date", nullable: true);
        migrationBuilder.Sql(UpSql);
        migrationBuilder.Sql(GuardedViewsWithIntroducedOn);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuardedViews);
        migrationBuilder.Sql(DownSql);
        migrationBuilder.DropColumn(name: "introduced_on", table: "action_revisions");
    }
}
