using CivicLens.Infrastructure.Publication;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// The hand SQL of the baseline, in dependency order: the notify functions, their triggers, the constraints and indexes that the model does
/// not hold, the stale column defaults that production keeps, the <c>public_api</c> schema and its owner role, and every public view in its
/// final text. <c>PublicApiGrants</c> grants the base columns to the view owner and the views to <c>civic_lens_public</c> after each migrate.
/// <c>MigrationHistorySquash</c> moves a database with the pre-squash history to this baseline without running it.
/// </summary>
public partial class InitialSchema
{
    private const string NotifyFunctions = """
        CREATE FUNCTION civic_lens_notify_analysis_change() RETURNS trigger AS $$
        BEGIN
            PERFORM pg_notify('civic_lens_analysis', TG_TABLE_NAME || ':' || NEW.task);
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE FUNCTION civic_lens_notify_work_change() RETURNS trigger AS $$
        BEGIN
            PERFORM pg_notify('civic_lens_work', 'changed');
            IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END;
        $$ LANGUAGE plpgsql;

        CREATE FUNCTION civic_lens_notify_publishing_approval() RETURNS trigger AS $$
        BEGIN
            PERFORM pg_notify('civic_lens_publishing',
                ((EXTRACT(EPOCH FROM clock_timestamp() + interval '60 seconds')::numeric * 10000000
                    + 621355968000000000)::bigint)::text);
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;
        """;

    private const string NotifyTriggers = """
        CREATE TRIGGER trg_action_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON action_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_brief_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON brief_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_collection_collector_slot_notify
        AFTER INSERT OR DELETE OR UPDATE OF job_id, attempt_id ON collection_collector_slot
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_collection_job_origins_notify
        AFTER INSERT OR DELETE OR UPDATE ON collection_job_origins
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_collection_jobs_notify
        AFTER INSERT OR DELETE OR UPDATE OF state, retry_at ON collection_jobs
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_collection_source_schedules_notify
        AFTER INSERT OR DELETE OR UPDATE OF enabled, configuration_revision_id, interval_seconds, next_due_utc_ticks ON collection_source_schedules
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_document_change_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON document_change_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_document_extractions_notify
        AFTER INSERT ON document_extractions
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_evidence_processing_notify
        AFTER INSERT OR DELETE OR UPDATE OF status, retry_at ON evidence_processing
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

        CREATE TRIGGER trg_relationship_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON relationship_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_statement_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON statement_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_vote_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON vote_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

        CREATE TRIGGER trg_action_drafts_publishing_insert_notify
        AFTER INSERT ON action_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_action_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON action_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_brief_drafts_publishing_insert_notify
        AFTER INSERT ON brief_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_brief_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON brief_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_document_change_drafts_publishing_insert_notify
        AFTER INSERT ON document_change_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_document_change_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON document_change_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_executive_action_drafts_publishing_insert_notify
        AFTER INSERT ON executive_action_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_executive_action_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON executive_action_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_relationship_drafts_publishing_insert_notify
        AFTER INSERT ON relationship_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_relationship_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON relationship_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_statement_drafts_publishing_insert_notify
        AFTER INSERT ON statement_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_statement_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON statement_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_vote_drafts_publishing_insert_notify
        AFTER INSERT ON vote_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved')
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_vote_drafts_publishing_notify
        AFTER UPDATE OF review_status, current_revision_number ON vote_drafts
        FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
            (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
        EXECUTE FUNCTION civic_lens_notify_publishing_approval();

        CREATE TRIGGER trg_exec_summary_analysis_notify
        AFTER INSERT OR UPDATE OF status, retry_at ON exec_summary_analysis
        FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
        """;

    /// <summary>The composite prior-capture foreign key, and the check that a new attempt records its elapsed time. Production holds the
    /// check with the newest observation time at its first migrate as the cutoff. A new database has no older attempt, so its cutoff is 0.</summary>
    private const string HandConstraints = """
        ALTER TABLE collection_attempts
        ADD CONSTRAINT fk_collection_attempts_prior_capture_observation
        FOREIGN KEY (prior_capture_attempt_id, prior_capture_sha256)
        REFERENCES collection_attempts (attempt_id, capture_sha256)
        ON DELETE RESTRICT;

        ALTER TABLE collection_attempts
        ADD CONSTRAINT ck_collection_attempts_elapsed_measured
        CHECK (elapsed_milliseconds IS NOT NULL OR observed_at_utc_ticks <= 0);
        """;

    /// <summary>The descending, partial, and expression indexes.</summary>
    private const string HandIndexes = """
        CREATE INDEX ix_action_drafts_approved_created ON action_drafts (created_at_utc_ticks DESC, record_id DESC) WHERE review_status = 'Approved';
        CREATE INDEX ix_collection_jobs_created_desc ON collection_jobs (created_at DESC, job_id);
        CREATE INDEX ix_document_change_decisions_created ON document_change_review_decisions (created_at_utc_ticks DESC);
        CREATE INDEX ix_document_change_drafts_approved_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status = 'Approved';
        CREATE INDEX ix_document_change_drafts_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC);
        CREATE INDEX ix_document_change_drafts_needs_action_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status <> 'Approved';
        CREATE INDEX ix_document_change_revisions_created ON document_change_revisions (created_at_utc_ticks DESC) WHERE revision_number > 1;
        CREATE INDEX ix_document_comparisons_has_changes ON document_comparisons (comparison_id) WHERE has_changes;
        CREATE INDEX ix_evidence_processing_changed ON evidence_processing (observed_at_utc_ticks DESC) WHERE outcome = 'Changed';
        CREATE INDEX ix_statement_drafts_approved_created ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status = 'Approved';
        CREATE INDEX ix_statement_drafts_needs_action_created ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status <> 'Approved';
        CREATE UNIQUE INDEX ux_action_revision_key_terms_term
            ON action_revision_key_terms (record_id, revision_number, lower(term));
        """;

    /// <summary>Defaults that earlier migrations added with new columns. The model has none, so the code always writes these columns.
    /// Production keeps them, so a new database keeps them too.</summary>
    private const string StaleColumnDefaults = """
        ALTER TABLE action_candidates ALTER COLUMN includes_subjects SET DEFAULT false;
        ALTER TABLE action_drafts ALTER COLUMN last_revised_at_utc_ticks SET DEFAULT 0;
        ALTER TABLE document_change_drafts ALTER COLUMN last_revised_at_utc_ticks SET DEFAULT 0;
        ALTER TABLE statement_drafts ALTER COLUMN last_revised_at_utc_ticks SET DEFAULT 0;
        ALTER TABLE vote_drafts ALTER COLUMN last_revised_at_utc_ticks SET DEFAULT 0;
        """;

    /// <summary>The role stays when the schema drops, because a role belongs to the cluster and other databases can use it.</summary>
    private const string PublicApiSchema = """
        DO $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public_owner') THEN
            CREATE ROLE civic_lens_public_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
          END IF;
        END $$;

        CREATE SCHEMA public_api AUTHORIZATION civic_lens_public_owner;
        """;

    /// <summary>Every <c>public_api</c> view, in dependency order. Each one is a security barrier view.</summary>
    private static readonly string[] PublicApiViews =
    [
        """
        CREATE VIEW public_api.active_records WITH (security_barrier = true) AS
        SELECT s.kind, s.record_id, s.revision_number, r.release_number, r.published_at_utc_ticks
          FROM public.publication_releases r
          JOIN public.publication_record_states s
            ON s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.vote_issues WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, i.issue_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;
        """,

        """
        CREATE VIEW public_api.bill_sponsorships WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, p."position", p.person_id AS official_id, p.role, p.is_original, p.sponsored_on, p.withdrawn_on
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.release_officials WITH (security_barrier = true) AS
        SELECT o.release_number, o.official_id, o.name, o.wikidata_id, o.website
          FROM public.release_officials o JOIN public.publication_releases r ON r.release_number = o.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.release_official_terms WITH (security_barrier = true) AS
        SELECT t.release_number, t.official_id, t."position", t.office, t.kind, t.state, t.district, t.party, t.caucus, t.start_on, t.end_on
          FROM public.release_official_terms t JOIN public.publication_releases r ON r.release_number = t.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.release_issue_includes WITH (security_barrier = true) AS
        SELECT i.release_number, i.issue_id, i."position", i.text
          FROM public.release_issue_includes i JOIN public.publication_releases r ON r.release_number = i.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.release_issue_excludes WITH (security_barrier = true) AS
        SELECT i.release_number, i.issue_id, i."position", i.text
          FROM public.release_issue_excludes i JOIN public.publication_releases r ON r.release_number = i.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.release_policy_areas WITH (security_barrier = true) AS
        SELECT a.release_number, a.policy_area_id, a.name
          FROM public.release_policy_areas a JOIN public.publication_releases r ON r.release_number = a.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.zip_districts WITH (security_barrier = true) AS
        WITH district_sources AS (
          SELECT DISTINCT a.source_id
            FROM public.zcta_districts z
            JOIN public.collection_attempts a ON a.attempt_id = z.attempt_id),
        snapshots AS (
          SELECT a.attempt_id, a.source_id, a.capture_sha256, a.observed_at_utc_ticks
            FROM public.collection_attempts a
            JOIN public.roster_snapshots s ON s.attempt_id = a.attempt_id
           WHERE a.source_id IN (SELECT source_id FROM district_sources)),
        newest_capture AS (
          SELECT DISTINCT ON (a.source_id) a.source_id, a.capture_sha256
            FROM public.collection_attempts a
           WHERE a.source_id IN (SELECT source_id FROM district_sources)
             AND EXISTS (SELECT 1 FROM snapshots s WHERE s.source_id = a.source_id AND s.capture_sha256 = a.capture_sha256)
           ORDER BY a.source_id, a.observed_at_utc_ticks DESC, a.attempt_id DESC),
        newest_snapshot AS (
          SELECT DISTINCT ON (s.source_id) s.attempt_id
            FROM snapshots s
            JOIN newest_capture c ON c.source_id = s.source_id AND c.capture_sha256 = s.capture_sha256
           ORDER BY s.source_id, s.observed_at_utc_ticks, s.attempt_id)
        SELECT z.zcta, z.state, z.district, z.land_area_square_meters,
               z.land_area_square_meters::numeric / sum(z.land_area_square_meters) OVER (PARTITION BY z.attempt_id, z.zcta) AS land_share,
               119 AS congress
          FROM public.zcta_districts z
          JOIN newest_snapshot n ON n.attempt_id = z.attempt_id;
        """,

        """
        CREATE VIEW public_api.official_issue_counts WITH (security_barrier = true) AS
        SELECT p.person_id AS official_id, r.congress, i.issue_id, 'bill'::text AS source, NULL::text AS vote_kind,
               count(DISTINCT p.record_id) AS records
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number AND p.role = 'Sponsor'
          JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number AND i.is_tracked
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
         GROUP BY p.person_id, r.congress, i.issue_id
        UNION ALL
        SELECT e.person_id, v.congress, i.issue_id, 'vote'::text, v.kind::text, count(*)
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id AND e.vote_cast <> 'Not Voting'
          JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked
         WHERE a.kind = 'vote'
         GROUP BY e.person_id, v.congress, i.issue_id, v.kind;
        """,

        """
        CREATE VIEW public_api.official_executive_actions WITH (security_barrier = true) AS
        SELECT e.official_id, p.document_type, count(*) AS actions
          FROM public_api.active_records a
          JOIN public.published_entries p ON p.kind = a.kind AND p.record_id = a.record_id AND p.revision_number = a.revision_number
          JOIN public.published_entry_officials e ON e.kind = p.kind AND e.record_id = p.record_id AND e.revision_number = p.revision_number
         WHERE a.kind = 'executiveAction'
         GROUP BY e.official_id, p.document_type;
        """,

        """
        CREATE VIEW public_api.executive_action_notices WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, n."position", n.document_number, d.published_on, d.notice_url
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.executive_action_revision_notices n ON n.record_id = r.record_id AND n.revision_number = r.revision_number
          JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL AND d.notice_url IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.issue_sponsors WITH (security_barrier = true) AS
        SELECT i.issue_id, p.person_id AS official_id, r.event_on AS event_date
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
          JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked AND p.role = 'Sponsor';
        """,

        """
        CREATE VIEW public_api.withdrawals WITH (security_barrier = true) AS
        SELECT r.release_number, w.kind, w.record_id, w.withdrawn_revision_number,
               p.first_published_at_utc_ticks, w.withdrawn_at_utc_ticks, w.note, w.first_release
          FROM public.publication_releases r
          JOIN public.publication_withdrawals w
            ON w.first_release <= r.release_number AND (w.last_release IS NULL OR w.last_release >= r.release_number)
          JOIN public.published_records p ON p.kind = w.kind AND p.record_id = w.record_id
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.issue_votes WITH (security_barrier = true) AS
        SELECT i.issue_id, v.kind, v.congress
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;
        """,

        """
        CREATE VIEW public_api.bill_issues WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, i.issue_id, i."position", r.congress
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                        WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);
        """,

        """
        CREATE VIEW public_api.active_release WITH (security_barrier = true) AS
        SELECT release_number, published_at_utc_ticks
          FROM public.publication_releases
         WHERE is_active;
        """,

        """
        CREATE VIEW public_api.party_agreement WITH (security_barrier = true) AS
        SELECT a.release_number, v.chamber, v.congress, v.kind,
               count(*) FILTER (WHERE v.alignment = 'PartiesAgree') AS agreed,
               count(*) FILTER (WHERE v.alignment = 'Split') AS split,
               count(*) FILTER (WHERE v.alignment = 'Mixed') AS mixed
          FROM public_api.active_records a
          LEFT JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
         WHERE a.kind = 'vote'
         GROUP BY a.release_number, v.chamber, v.congress, v.kind;
        """,

        """
        CREATE VIEW public_api.document_changes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, r.comparison_id, c.hunk_count,
               r.headline, r.summary,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Person' THEN 'Person' END AS text_origin,
               CASE r.author_kind WHEN 'Analysis' THEN 'Drafted by an AI model from these source versions. A person checked it before publication.' END AS label,
               r.significance, r.limits,
               r.institution, r.change_date,
               ARRAY(SELECT i.issue_id FROM public.document_change_revision_issues i
                     WHERE i.draft_id = a.record_id AND i.revision_number = a.revision_number
                     ORDER BY i.position, i.issue_id LIMIT 65) AS issue_ids
          FROM public_api.active_records a
          JOIN public.document_change_revisions r ON r.draft_id = a.record_id AND r.revision_number = a.revision_number
          LEFT JOIN public.document_comparisons c ON c.comparison_id = r.comparison_id
         WHERE a.kind = 'documentChange';
        """,

        """
        CREATE VIEW public_api.document_change_officials WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, o.position, o.official_id
          FROM public_api.document_changes d
          JOIN public.document_change_revision_officials o
            ON o.draft_id = d.record_id AND o.revision_number = d.revision_number;
        """,

        """
        CREATE VIEW public_api.document_change_sources WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, 'Before'::text AS source_side, c.before_extraction_id AS extraction_id,
               a.final_url AS url, a.observed_at_utc_ticks, e.text
          FROM public_api.document_changes d
          JOIN public.document_comparisons c ON c.comparison_id = d.comparison_id
          JOIN public.document_extractions e ON e.extraction_id = c.before_extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id
        UNION ALL
        SELECT d.record_id, d.revision_number, 'After'::text, c.after_extraction_id,
               a.final_url, a.observed_at_utc_ticks, e.text
          FROM public_api.document_changes d
          JOIN public.document_comparisons c ON c.comparison_id = d.comparison_id
          JOIN public.document_extractions e ON e.extraction_id = c.after_extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id;
        """,

        """
        CREATE VIEW public_api.document_change_hunks WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, h.position, h.before_start, h.before_length, h.after_start, h.after_length,
               h.before_context_start, h.before_context_length, h.after_context_start, h.after_context_length, h.word_edit_count
          FROM public_api.document_changes d
          JOIN public.document_comparison_hunks h ON h.comparison_id = d.comparison_id;
        """,

        """
        CREATE VIEW public_api.document_change_word_edits WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, e.hunk_position, e.position, e.before_start, e.before_length, e.after_start, e.after_length
          FROM public_api.document_changes d
          JOIN public.document_comparison_word_edits e ON e.comparison_id = d.comparison_id;
        """,

        """
        CREATE VIEW public_api.document_change_citations WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, c.position, c.citation_extraction_id AS extraction_id,
               c.citation_start, c.citation_length, c.is_change_date_evidence
          FROM public_api.document_changes d
          JOIN public.document_change_revision_citations c
            ON c.draft_id = d.record_id AND c.revision_number = d.revision_number;
        """,

        """
        CREATE VIEW public_api.release_issues WITH (security_barrier = true) AS
        SELECT i.release_number, i.issue_id, i.name, i.definition, i.count_congress, i.substantive_votes, i.bills
          FROM public.release_issues i JOIN public.publication_releases r ON r.release_number = i.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.vote_first_publications WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, min(s.first_release) AS first_release
          FROM public_api.active_records a
          JOIN public.publication_record_states s ON s.kind = 'vote' AND s.record_id = a.record_id
                                                   AND s.first_release <= a.release_number
         WHERE a.kind = 'vote'
         GROUP BY a.record_id, a.revision_number, a.release_number;
        """,

        """
        CREATE VIEW public_api.record_correction_histories WITH (security_barrier = true) AS
        SELECT r.release_number, c.kind, c.record_id, c.revision_number,
               h.history_id, h.encoding_version, h.correction_count
          FROM public.publication_releases r
          JOIN public.release_correction_histories c ON c.release_number = r.release_number
          JOIN public.publication_record_states s
            ON s.kind = c.kind AND s.record_id = c.record_id AND s.revision_number = c.revision_number
           AND s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
          JOIN public.published_correction_histories h ON h.history_id = c.history_id
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.vote_party_tallies WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, v.party_tally_count,
               t.party, t.yea, t.nay, t.present, t.not_voting, t.other
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.vote_revision_party_tallies t
                 ON t.record_id = v.record_id AND t.revision_number = v.revision_number
         WHERE a.kind = 'vote';
        """,

        """
        CREATE VIEW public_api.vote_facts WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, f."position", f.fact_text,
               f.citation_extraction_id AS extraction_id, f.citation_start, f.citation_length
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_revision_facts f ON f.record_id = v.record_id AND f.revision_number = v.revision_number
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.vote_evidence WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, v.vote_source,
               v.voted_on_citation_extraction_id, v.voted_on_citation_start, v.voted_on_citation_length,
               v.kind_citation_extraction_id, v.kind_citation_start, v.kind_citation_length
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.release_issue_party_lineups WITH (security_barrier = true) AS
        SELECT l.release_number, l.issue_id, l.unknown_bills, c.party, c.bills
          FROM public.release_issue_party_lineups l
          JOIN public.publication_releases r ON r.release_number = l.release_number
          LEFT JOIN public.release_issue_party_counts c ON c.release_number = l.release_number AND c.issue_id = l.issue_id
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.executive_actions WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.title AS headline, r.kind, r.document_number, r.signed_on, r.published_on, r.federal_register_citation, r.document_url,
               r.person_id AS official_id, r.president_name, r.disposition_notes
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.executive_action_targets WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, t."position", t.relation, t.kind, t.number,
               t.reference_start, t.reference_length,
               (SELECT CASE WHEN count(*) = 1 THEN min(m.record_id) END
                  FROM public_api.active_records m
                  JOIN public.executive_action_revisions mr ON mr.record_id = m.record_id AND mr.revision_number = m.revision_number
                 WHERE m.kind = 'executiveAction' AND mr.title IS NOT NULL AND m.record_id <> a.record_id
                   AND mr.kind = t.kind AND mr.presidential_number = t.number) AS target_record_id
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.executive_action_revision_disposition_targets t ON t.record_id = r.record_id AND t.revision_number = r.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.release_bill_counts WITH (security_barrier = true) AS
        SELECT c.release_number, c.congress, c.bills, c.expected_member_row_count
          FROM public.release_bill_counts c
          JOIN public.publication_releases r ON r.release_number = c.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.release_official_bill_counts WITH (security_barrier = true) AS
        SELECT m.release_number, m.congress, m.official_id, m.sponsored, m.cosponsored
          FROM public.release_official_bill_counts m
          JOIN public.publication_releases r ON r.release_number = m.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, e."position", e.person_id AS official_id, e.vote_cast,
        e.party, term.caucus, e.citation_extraction_id AS extraction_id, e.citation_start, e.citation_length, term.state, term.district
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
          LEFT JOIN LATERAL (
               SELECT x.caucus, x.state, x.district
                 FROM public.release_official_terms x
                WHERE x.release_number = a.release_number AND x.official_id = e.person_id
                  AND x.start_on <= v.voted_on AND (x.end_on IS NULL OR x.end_on >= v.voted_on)
                  AND ((v.chamber = 'House' AND x.kind = 'Representative') OR (v.chamber = 'Senate' AND x.kind = 'Senator'))
                ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) term ON TRUE
         WHERE a.kind = 'vote';
        """,

        """
        CREATE VIEW public_api.executive_action_sources WITH (security_barrier = true) AS
        SELECT e.record_id, e.revision_number, cited."position", cited.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
          FROM public_api.executive_actions e
          JOIN public.executive_action_revisions r ON r.record_id = e.record_id AND r.revision_number = e.revision_number
          CROSS JOIN LATERAL (
              SELECT c.extraction_id, (row_number() OVER (ORDER BY min(c.rank)) - 1)::integer AS "position"
                FROM (SELECT f.citation_extraction_id AS extraction_id, ARRAY[0, f."position"] AS rank
          FROM public.executive_action_revision_facts f
         WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number
        UNION ALL
        SELECT r.disposition_notes_extraction_id, ARRAY[1, 0] WHERE r.disposition_notes_extraction_id IS NOT NULL
        UNION ALL
        SELECT d.notice_citation_extraction_id, ARRAY[2, n."position"]
          FROM public.executive_action_revision_notices n
          JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
         WHERE n.record_id = r.record_id AND n.revision_number = r.revision_number) c
               GROUP BY c.extraction_id) cited
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = cited.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        """,

        """
        CREATE VIEW public_api.vote_sources WITH (security_barrier = true) AS
        SELECT v.record_id, v.revision_number, cited.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text, cited."position"
          FROM public_api.vote_evidence v
          CROSS JOIN LATERAL (
              SELECT c.extraction_id, (row_number() OVER (ORDER BY min(c.rank)) - 1)::integer AS "position"
                FROM (SELECT v.voted_on_citation_extraction_id AS extraction_id, ARRAY[0, 0] AS rank
        UNION ALL
        SELECT f.extraction_id, ARRAY[1, f."position"] FROM public_api.vote_facts f
         WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
        UNION ALL
        SELECT v.kind_citation_extraction_id, ARRAY[2, 0] WHERE v.kind_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT m.extraction_id, ARRAY[3, m."position"] FROM public_api.vote_members m
         WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number) c
               GROUP BY c.extraction_id) cited
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = cited.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        """,

        """
        CREATE VIEW public_api.record_officials WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, c."position", c.official_id, c.is_matched
          FROM public_api.active_records a
          JOIN public.published_entry_officials c ON c.kind = a.kind AND c.record_id = a.record_id AND c.revision_number = a.revision_number;
        """,

        """
        CREATE VIEW public_api.record_issues WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, c."position", c.issue_id
          FROM public_api.active_records a
          JOIN public.published_entry_issues c ON c.kind = a.kind AND c.record_id = a.record_id AND c.revision_number = a.revision_number;
        """,

        """
        CREATE VIEW public_api.record_policy_areas WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, c."position", c.policy_area_id
          FROM public_api.active_records a
          JOIN public.published_entry_policy_areas c ON c.kind = a.kind AND c.record_id = a.record_id AND c.revision_number = a.revision_number;
        """,

        """
        CREATE VIEW public_api.record_first_publications WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, p.first_published_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.published_records p ON p.kind = a.kind AND p.record_id = a.record_id;
        """,

        """
        CREATE VIEW public_api.briefs WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, r.week_start, r.headline, r.text_origin,
               CASE r.text_origin WHEN 'Model' THEN 'Drafted by an AI model from reviewed records. A person checked it before publication.' END AS label,
               r.cutoff_release_number, r.coverage_considered, r.coverage_sent_to_model,
               (SELECT e.approved_at_utc_ticks FROM public.published_entries e
                 WHERE e.kind = 'brief' AND e.record_id = a.record_id AND e.revision_number = a.revision_number) AS approved_at_utc_ticks,
               (SELECT p.first_published_at_utc_ticks FROM public.published_records p
                 WHERE p.kind = 'brief' AND p.record_id = a.record_id) AS first_published_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.brief_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'brief' AND r.week_start IS NOT NULL AND r.headline IS NOT NULL AND r.text_origin IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.brief_paragraphs WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.release_number, p.position, p.paragraph_text,
               ARRAY(SELECT pc.citation_id FROM public.brief_revision_paragraph_citations pc
                      WHERE pc.record_id = p.record_id AND pc.revision_number = p.revision_number
                        AND pc.part = 'Text' AND pc.paragraph_position = p.position
                      ORDER BY pc.position) AS citation_ids
          FROM public_api.briefs b
          JOIN public.brief_revision_paragraphs p ON p.record_id = b.record_id AND p.revision_number = b.revision_number
         WHERE p.part = 'Text';
        """,

        """
        CREATE VIEW public_api.brief_citations WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.release_number, c.position, c.citation_id,
               CASE c.kind WHEN 'Statement' THEN 'statement' WHEN 'Action' THEN 'action' WHEN 'DocumentChange' THEN 'documentChange' END AS kind,
               CASE c.kind WHEN 'Statement' THEN c.statement_candidate_id WHEN 'Action' THEN c.action_record_id WHEN 'DocumentChange' THEN c.document_change_draft_id END AS target_record_id,
               CASE c.kind WHEN 'Statement' THEN c.statement_revision_number WHEN 'Action' THEN c.action_revision_number WHEN 'DocumentChange' THEN c.document_change_revision_number END AS target_revision_number,
               CASE c.kind WHEN 'Statement' THEN s.headline WHEN 'Action' THEN ar.headline WHEN 'DocumentChange' THEN d.headline END AS headline
          FROM public_api.briefs b
          JOIN public.brief_revision_citations c ON c.record_id = b.record_id AND c.revision_number = b.revision_number AND c.part = 'Text'
          LEFT JOIN public.statement_revisions s ON c.kind = 'Statement' AND s.candidate_id = c.statement_candidate_id AND s.revision_number = c.statement_revision_number
          LEFT JOIN public.action_revisions ar ON c.kind = 'Action' AND ar.record_id = c.action_record_id AND ar.revision_number = c.action_revision_number
          LEFT JOIN public.document_change_revisions d ON c.kind = 'DocumentChange' AND d.draft_id = c.document_change_draft_id AND d.revision_number = c.document_change_revision_number;
        """,

        """
        CREATE VIEW public_api.executive_action_summaries WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.summary_text_origin AS text_origin,
               CASE WHEN r.summary_text_origin = 'Model'
                    THEN 'An AI model summarized the document text. Code checks each quote.' END AS label,
               r.summary_cut AS cut
          FROM public_api.active_records a
          JOIN public.executive_action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'executiveAction' AND r.title IS NOT NULL AND r.summary_text_origin IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.bill_status WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, s.latest_action_text, s.latest_action_date, s.read_at_utc_ticks, s.status
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.release_bill_statuses s ON s.release_number = a.release_number AND s.record_id = a.record_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                        WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);
        """,

        $$"""
        CREATE VIEW public_api.bill_sponsorship_parties WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", term.party
          FROM public_api.bill_sponsorships s
        {{TermParties.PartyOn(TermParties.PublicTerms, "s.official_id", "s.sponsored_on")}};
        """,

        """
        CREATE VIEW public_api.executive_action_facts WITH (security_barrier = true) AS
        SELECT e.record_id, e.revision_number,
               (row_number() OVER (PARTITION BY e.record_id, e.revision_number ORDER BY c.rank) - 1)::integer AS "position",
               c.extraction_id, c.citation_start, c.citation_length
          FROM public_api.executive_actions e
          JOIN public.executive_action_revisions r ON r.record_id = e.record_id AND r.revision_number = e.revision_number
          CROSS JOIN LATERAL (
              SELECT f.citation_extraction_id AS extraction_id, f.citation_start, f.citation_length, ARRAY[0, f."position"] AS rank
                FROM public.executive_action_revision_facts f
               WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number
              UNION ALL
              SELECT r.disposition_notes_extraction_id, r.disposition_notes_start, r.disposition_notes_length, ARRAY[1, 0]
               WHERE r.disposition_notes_extraction_id IS NOT NULL
              UNION ALL
              SELECT d.notice_citation_extraction_id, d.notice_citation_start, d.notice_citation_length, ARRAY[2, n."position"]
                FROM public.executive_action_revision_notices n
                JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
               WHERE n.record_id = r.record_id AND n.revision_number = r.revision_number) c;
        """,

        """
        CREATE VIEW public_api.executive_action_summary_sentences WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", s.sentence, s.quote_extraction_id AS extraction_id, s.quote_start, s.quote_length
          FROM public_api.executive_action_summaries m
          JOIN public.executive_action_revision_summary_sentences s ON s.record_id = m.record_id AND s.revision_number = m.revision_number
         WHERE m.text_origin = 'Model';
        """,

        """
        CREATE VIEW public_api.executive_action_summary_sources WITH (security_barrier = true) AS
        SELECT q.record_id, q.revision_number, q.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
          FROM (SELECT DISTINCT s.record_id, s.revision_number, s.extraction_id FROM public_api.executive_action_summary_sentences s) q
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = q.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        """,

        """
        CREATE VIEW public_api.bill_key_terms WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, k."position", k.term, k.definition, k.cut,
               'Key terms chosen by an AI model. Each definition is an exact quote from the definitions section of the bill. Code checked the quotes, not the choice of terms.' AS label,
               regexp_replace(substr(regexp_replace(left(d.text, k.citation_start + k.citation_length), '([\U00010000-\U0010FFFF])', '\1\1', 'g'), k.citation_start + 1, k.citation_length),
                       '([\U00010000-\U0010FFFF])\1', '\1', 'g') AS quote
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_key_terms k ON k.record_id = r.record_id AND k.revision_number = r.revision_number
          JOIN public.document_extractions d ON d.extraction_id = k.citation_extraction_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND r.text_origin = 'Model' AND r.text_package_id IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.bill_text_sentences WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, x."position", x.part, x.sentence,
               regexp_replace(substr(regexp_replace(left(d.text, x.citation_start + x.citation_length), '([\U00010000-\U0010FFFF])', '\1\1', 'g'), x.citation_start + 1, x.citation_length),
                       '([\U00010000-\U0010FFFF])\1', '\1', 'g') AS quote
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_text_citations x ON x.record_id = r.record_id AND x.revision_number = r.revision_number
          JOIN public.document_extractions d ON d.extraction_id = x.citation_extraction_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND r.text_origin = 'Model' AND r.text_package_id IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
        SELECT e.person_id AS official_id, a.record_id,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline,
               CASE v.text_origin
                 WHEN 'Model' THEN 'An AI summary of the official record'
                 WHEN 'Code' THEN CASE v.chamber WHEN 'Senate' THEN 'Written automatically from Senate roll call data, with no AI' ELSE 'Written automatically from Congress.gov data, with no AI' END
               END AS label,
               v.result, e.vote_cast,
               ARRAY(SELECT i.issue_id::text FROM public.vote_revision_issues i
                      WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids,
               CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' WHEN 'Nay' THEN 'Nay' WHEN 'No' THEN 'Nay' END AS side,
               CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END AS party_majority_cast,
               e.party,
               v.alignment,
               v.columns_written AS alignment_known,
               CASE WHEN v.alignment = 'Split' AND e.party IN ('Democrat', 'Republican')
                         AND e.vote_cast IN ('Yea', 'Aye', 'Nay', 'No')
                         AND CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END IS NOT NULL
                    THEN (CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' ELSE 'Nay' END)
                         <> CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END
               END AS breaks_party,
               v.text_origin
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote';
        """,

        """
        CREATE VIEW public_api.official_vote_parties WITH (security_barrier = true) AS
        SELECT g.official_id, g.chamber, g.congress, g.party,
               (sum(g.party_held) OVER seat)::bigint AS held,
               (sum(g.party_missed) OVER seat)::bigint AS missed,
               min(g.party_first_voted_on) OVER seat AS first_voted_on,
               max(g.party_last_voted_on) OVER seat AS last_voted_on,
               g.with_party, g.against_party, g.cast_count, g.first_cast_on, g.last_cast_on, g.unwritten_votes
          FROM (
              SELECT m.official_id, m.chamber, m.congress, m.party,
                     count(*) AS party_held,
                     count(*) FILTER (WHERE m.vote_cast = 'Not Voting') AS party_missed,
                     min(m.voted_on) AS party_first_voted_on, max(m.voted_on) AS party_last_voted_on,
                     count(*) FILTER (WHERE NOT m.breaks_party) AS with_party,
                     count(*) FILTER (WHERE m.breaks_party) AS against_party,
                     count(*) FILTER (WHERE m.breaks_party IS NOT NULL) AS cast_count,
                     min(m.voted_on) FILTER (WHERE m.breaks_party IS NOT NULL) AS first_cast_on,
                     max(m.voted_on) FILTER (WHERE m.breaks_party IS NOT NULL) AS last_cast_on,
                     count(*) FILTER (WHERE NOT m.alignment_known) AS unwritten_votes
                FROM public_api.member_votes m
               GROUP BY m.official_id, m.chamber, m.congress, m.party) g
        WINDOW seat AS (PARTITION BY g.official_id, g.chamber, g.congress);
        """,

        """
        CREATE VIEW public_api.official_attendance WITH (security_barrier = true) AS
        SELECT DISTINCT p.official_id, p.chamber, p.congress, p.held, p.missed, p.first_voted_on, p.last_voted_on
          FROM public_api.official_vote_parties p;
        """,

        """
        CREATE VIEW public_api.official_party_lines WITH (security_barrier = true) AS
        SELECT p.official_id, p.chamber, p.congress, p.party, p.with_party, p.against_party, p.cast_count,
               p.first_cast_on AS first_voted_on, p.last_cast_on AS last_voted_on
          FROM public_api.official_vote_parties p
         WHERE p.cast_count > 0;
        """,

        """
        CREATE VIEW public_api.statements WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.statement_date, r.headline, r.summary,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Code' THEN 'Code' ELSE 'Person' END AS text_origin,
               CASE r.author_kind WHEN 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN 'An AI summary of this press release.' WHEN 'Person' THEN 'Drafted by an AI model from this source. A person checked it before publication.' END
                    WHEN 'Code' THEN 'The source text of this press release, with no AI.'
               END AS label,
               ARRAY(SELECT i.issue_id FROM public.statement_revision_issues i
                     WHERE i.candidate_id = r.candidate_id AND i.revision_number = r.revision_number ORDER BY i."position") AS issue_ids
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND r.summary IS NOT NULL AND e.approved_by IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.statement_revision_speakers s
                        WHERE s.candidate_id = r.candidate_id AND s.revision_number = r.revision_number);
        """,

        """
        CREATE VIEW public_api.statement_speakers WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, p."position", p.official_id
          FROM public_api.statements s
          JOIN public.statement_revision_speakers p ON p.candidate_id = s.record_id AND p.revision_number = s.revision_number;
        """,

        """
        CREATE VIEW public_api.statement_sources WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, c.extraction_id, a.requested_url AS url,
               a.observed_at_utc_ticks, e.text,
               CASE WHEN c.url <> a.requested_url THEN c.url END AS article_url,
               r.statement_date_evidence_extraction_id, r.statement_date_evidence_start, r.statement_date_evidence_length
          FROM public_api.statements s
          JOIN public.statement_candidates c ON c.candidate_id = s.record_id
          JOIN public.statement_revisions r ON r.candidate_id = s.record_id AND r.revision_number = s.revision_number
          JOIN public.document_extractions e ON e.extraction_id = c.extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id AND a.attempt_id = c.attempt_id;
        """,

        """
        CREATE VIEW public_api.statement_quotations WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, q.position, q.quotation_extraction_id AS extraction_id,
               q.quotation_start, q.quotation_length, q.kind
          FROM public_api.statements s
          JOIN public.statement_revision_quotations q
            ON q.candidate_id = s.record_id AND q.revision_number = s.revision_number;
        """,

        """
        CREATE VIEW public_api.statement_approvers WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, e.approved_by
          FROM public_api.statements s
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = s.record_id AND e.revision_number = s.revision_number;
        """,

        $$"""
        CREATE VIEW public_api.statement_speaker_terms WITH (security_barrier = true) AS
        SELECT p.record_id, p.revision_number, p."position", term.party, seat.kind, seat.office, seat.state, seat.district
          FROM public_api.statement_speakers p
          JOIN public_api.statements s ON s.record_id = p.record_id AND s.revision_number = p.revision_number
        {{TermParties.PartyOn(TermParties.PublicTerms, "p.official_id", "s.statement_date")}}
          LEFT JOIN LATERAL (
               SELECT min(t.kind) AS kind, min(t.office) AS office, min(t.state) AS state, min(t.district) AS district
                 FROM public_api.release_official_terms t
                WHERE t.official_id = p.official_id
                  AND t.start_on <= s.statement_date AND (t.end_on IS NULL OR t.end_on >= s.statement_date)
               HAVING count(*) = 1) seat ON TRUE;
        """,

        """
        CREATE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, CASE WHEN right(e.bill_line, 1) <> '…' THEN e.bill_line END AS bill_line,
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
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        """,

        """
        CREATE VIEW public_api.bill_actions WITH (security_barrier = true) AS
        SELECT b.release_number, b.record_id, b.revision_number, a.position, a.action_date, a.action_time,
               a.text, a.type, a.action_code, a.source_system_code, a.source_system_name
          FROM public_api.bills b
          JOIN public.release_bill_histories r ON r.release_number = b.release_number AND r.record_id = b.record_id
          JOIN public.published_bill_history_actions a ON a.detail_job_id = r.detail_job_id AND a.projection_version = r.projection_version;
        """,

        """
        CREATE VIEW public_api.action_sources WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, cited."position", cited.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
          FROM public_api.bills b
          JOIN public.action_revisions r ON r.record_id = b.record_id AND r.revision_number = b.revision_number
          CROSS JOIN LATERAL (
              SELECT c.extraction_id, (row_number() OVER (ORDER BY min(c.rank)) - 1)::integer AS "position"
                FROM (SELECT p.citation_extraction_id AS extraction_id, ARRAY[0, p.sponsorship_position, p."position"] AS rank
          FROM public.action_revision_sponsorship_citations p
         WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number
        UNION ALL
        SELECT r.summary_citation_extraction_id, ARRAY[1, 0, 0] WHERE r.summary_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT r.policy_area_citation_extraction_id, ARRAY[2, 0, 0] WHERE r.policy_area_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT r.bill_citation_extraction_id, ARRAY[3, 0, 0] WHERE r.bill_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT x.citation_extraction_id, ARRAY[4, x."position", 0]
          FROM public.action_revision_text_citations x
         WHERE x.record_id = r.record_id AND x.revision_number = r.revision_number) c
               GROUP BY c.extraction_id) cited
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = cited.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        """,

        """
        CREATE VIEW public_api.votes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline, v.summary, v.text_origin,
               CASE v.text_origin
                 WHEN 'Model' THEN 'An AI summary of the official record'
                 WHEN 'Code' THEN CASE v.chamber WHEN 'Senate' THEN 'Written automatically from Senate roll call data, with no AI' ELSE 'Written automatically from Congress.gov data, with no AI' END
               END AS label,
               CASE WHEN t.other_count = 0 THEN t.yea_count::bigint END AS yea,
               CASE WHEN t.other_count = 0 THEN t.nay_count::bigint END AS nay,
               CASE WHEN t.other_count = 0 THEN t.present_count::bigint END AS present,
               CASE WHEN t.other_count = 0 THEN t.not_voting_count::bigint END AS not_voting,
               v.question, v.result,
               v.alignment,
               bill.record_id::varchar(64) AS bill_record_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
          LEFT JOIN LATERAL (SELECT b.record_id FROM public_api.bills b WHERE b.record_id = rb.bill_record_id LIMIT 1) bill ON TRUE
          JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.record_entries WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, e.headline, e.source_host, e.event_date, CASE WHEN right(e.bill_line, 1) <> '…' THEN e.bill_line END AS bill_line,
               e.bill_title, e.document_type, e.link_type, e.statement_kind, e.statement_record_id, e.target_kind, e.target_record_id, e.approved_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number;
        """,

        """
        CREATE VIEW public_api.record_correction_history_entries WITH (security_barrier = true) AS
        SELECT h.release_number, h.kind, h.record_id, h.revision_number, e."position",
               e.replaced_revision_number, e.published_at_utc_ticks, e.published_offset_minutes,
               e.note, e.text_rewrite
          FROM public_api.record_correction_histories h
          JOIN public.published_correction_history_entries e ON e.history_id = h.history_id;
        """,

        """
        CREATE VIEW public_api.withdrawal_corrections WITH (security_barrier = true) AS
        SELECT r.release_number, c.kind, c.record_id, c.first_release, c."position",
               c.replaced_revision_number, c.published_at_utc_ticks, c.note, c.text_rewrite
          FROM public.publication_releases r
          JOIN public.publication_withdrawals w
            ON w.first_release <= r.release_number AND (w.last_release IS NULL OR w.last_release >= r.release_number)
          JOIN public.publication_withdrawal_corrections c
            ON c.kind = w.kind AND c.record_id = w.record_id AND c.first_release = w.first_release
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.bill_keys WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.policy_area_id, b.event_date, s.status, s.latest_action_date
          FROM public_api.bills b
          LEFT JOIN public.release_bill_statuses s ON s.release_number = b.release_number AND s.record_id = b.record_id;
        """,

        """
        CREATE VIEW public_api.record_headlines WITH (security_barrier = true) AS
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
                 WHEN 'Code' THEN CASE r.chamber WHEN 'Senate' THEN 'Written automatically from Senate roll call data, with no AI' ELSE 'Written automatically from Congress.gov data, with no AI' END
               END
          FROM public_api.active_records a
          JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'vote' AND r.headline IS NOT NULL
        UNION ALL
        SELECT a.kind, a.record_id, r.headline, CASE r.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Code' THEN 'Code' ELSE 'Person' END, CASE r.author_kind WHEN 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN 'An AI summary of this press release.' WHEN 'Person' THEN 'Drafted by an AI model from this source. A person checked it before publication.' END
                    WHEN 'Code' THEN 'The source text of this press release, with no AI.'
               END
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND e.approved_by IS NOT NULL;
        """,

        """
        CREATE VIEW public_api.record_links WITH (security_barrier = true) AS
        SELECT l.statement_record_id AS record_id, 'statement' AS kind,
               t.kind AS related_kind, t.record_id AS related_record_id, h.headline AS related_headline, h.text_origin AS related_text_origin, h.label AS related_label,
               l.link_type, l.event_date AS statement_date, l.record_id AS link_record_id, r.reason,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length, l.approved_by AS approver
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          LEFT JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = t.kind AND x.record_id = t.record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement'
        UNION ALL
        SELECT l.target_record_id, l.target_kind,
               'statement', s.record_id, h.headline, h.text_origin, h.label,
               l.link_type, l.event_date AS statement_date, l.record_id AS link_record_id, r.reason,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length, l.approved_by
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          LEFT JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = l.statement_kind AND x.record_id = l.statement_record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement';
        """,

        """
        CREATE VIEW public_api.relationships WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, e.headline, e.link_type, e.event_date AS statement_date, r.reason,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' THEN 'Drafted by an AI model from these two records. A person checked it before publication.' END AS label,
               e.approved_by AS approver,
               e.statement_record_id, r.statement_revision_number, sh.headline AS statement_headline, sh.text_origin AS statement_text_origin, sh.label AS statement_label,
               e.target_kind, e.target_record_id, coalesce(r.target_action_revision_number, r.target_vote_revision_number) AS target_revision_number,
               th.headline AS target_headline, th.text_origin AS target_text_origin, th.label AS target_label,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length,
               x.text AS quote_source_text, f.fact, f.fact_label,
               f.fact_text_origin
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = 'relationship' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          JOIN public_api.active_records s ON s.kind = e.statement_kind AND s.record_id = e.statement_record_id
          JOIN public_api.active_records t ON t.kind = e.target_kind AND t.record_id = e.target_record_id
          JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          LEFT JOIN public.document_extractions x ON x.extraction_id = r.statement_quote_extraction_id
          LEFT JOIN LATERAL (SELECT h.headline, h.text_origin, h.label FROM public_api.record_headlines h
                              WHERE h.kind = e.statement_kind AND h.record_id = e.statement_record_id OFFSET 0) sh ON TRUE
          LEFT JOIN LATERAL (SELECT h.headline, h.text_origin, h.label FROM public_api.record_headlines h
                              WHERE h.kind = e.target_kind AND h.record_id = e.target_record_id OFFSET 0) th ON TRUE
          LEFT JOIN LATERAL (SELECT v.fact, v.fact_label, v.fact_text_origin FROM (SELECT 'F' || row_number() OVER (ORDER BY vf."position") AS fact_id, vf.fact_text AS fact, NULL::text AS fact_label, NULL::text AS fact_text_origin
          FROM public.vote_revision_facts vf
         WHERE r.target_kind = 'Vote' AND vf.record_id = r.target_vote_record_id AND vf.revision_number = r.target_vote_revision_number
        UNION ALL
        SELECT c.fact_id, c.fact, c.fact_label, c.fact_text_origin
          FROM public.action_revisions ar
          CROSS JOIN LATERAL (VALUES
              ('F1', 'Bill: ' || ar.bill_type || ' ' || ar.bill_number || ', Congress ' || ar.congress, NULL::text, NULL::text),
              ('F2', 'Title: ' || ar.bill_title, NULL, NULL),
              ('F3', 'Headline: ' || ar.headline, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin),
              ('F4', 'Summary: ' || ar.summary, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin)) c(fact_id, fact, fact_label, fact_text_origin)
         WHERE r.target_kind = 'Action' AND ar.record_id = r.target_action_record_id AND ar.revision_number = r.target_action_revision_number) v WHERE v.fact_id = r.target_fact_id) f ON TRUE
         WHERE a.kind = 'relationship' AND e.statement_kind = 'statement';
        """,

        """
        CREATE VIEW public_api.release_official_record_counts WITH (security_barrier = true) AS
        SELECT c.release_number, c.official_id, c.records, c.votes, c.bills, c.statements, c.executive_actions
          FROM public.release_official_record_counts c
          JOIN public.publication_releases r ON r.release_number = c.release_number
         WHERE r.is_active;
        """,

        """
        CREATE VIEW public_api.officials WITH (security_barrier = true) AS
        SELECT r.release_number, o.official_id, o.name, t.kind, t.office, t.state, t.district, t.party, t.caucus, t.end_on AS term_end_on,
               COALESCE(c.records, 0)::bigint AS record_count, o.photo_path,
               COALESCE(o.sort_name, o.name) AS sort_name
          FROM public.publication_releases r
          JOIN public.release_officials o ON o.release_number = r.release_number
          LEFT JOIN LATERAL (
               SELECT x.kind, x.office, x.state, x.district, x.party, x.caucus, x.end_on
                 FROM public.release_official_terms x
                WHERE x.release_number = o.release_number AND x.official_id = o.official_id
                  AND x.start_on <= DATE '0001-01-01' + (r.published_at_utc_ticks / 864000000000)::int
                ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) t ON TRUE
          LEFT JOIN public.release_official_record_counts c ON c.release_number = o.release_number AND c.official_id = o.official_id
         WHERE r.is_active;
        """
    ];

    private const string PublicApiViewOwners = """
        ALTER VIEW public_api.active_records OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_issues OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_sponsorships OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_officials OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_official_terms OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_issue_includes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_issue_excludes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_policy_areas OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.zip_districts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_issue_counts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_executive_actions OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_notices OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.issue_sponsors OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.withdrawals OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.issue_votes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_issues OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.active_release OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.party_agreement OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_changes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_change_officials OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_change_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_change_hunks OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_change_word_edits OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.document_change_citations OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_issues OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_first_publications OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_correction_histories OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_party_tallies OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_facts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_evidence OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_issue_party_lineups OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_actions OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_targets OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_bill_counts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_official_bill_counts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.vote_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_officials OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_issues OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_policy_areas OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_first_publications OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.briefs OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.brief_paragraphs OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.brief_citations OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_summaries OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_status OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_sponsorship_parties OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_facts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_summary_sentences OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.executive_action_summary_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_key_terms OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_text_sentences OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_vote_parties OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_attendance OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_party_lines OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statements OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statement_speakers OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statement_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statement_quotations OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statement_approvers OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.statement_speaker_terms OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_actions OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.action_sources OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_entries OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_correction_history_entries OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.withdrawal_corrections OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.bill_keys OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_headlines OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_links OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.relationships OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.release_official_record_counts OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.officials OWNER TO civic_lens_public_owner;
        """;

    private static void CreateHandWrittenObjects(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(NotifyFunctions);
        migrationBuilder.Sql(NotifyTriggers);
        migrationBuilder.Sql(HandConstraints);
        migrationBuilder.Sql(HandIndexes);
        migrationBuilder.Sql(StaleColumnDefaults);
        migrationBuilder.Sql(PublicApiSchema);
        foreach (var view in PublicApiViews)
            migrationBuilder.Sql(view);
        migrationBuilder.Sql(PublicApiViewOwners);
    }

    private static void DropHandWrittenObjects(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP SCHEMA public_api CASCADE;
        """);

    private static void DropNotifyFunctions(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION civic_lens_notify_analysis_change();
        DROP FUNCTION civic_lens_notify_publishing_approval();
        DROP FUNCTION civic_lens_notify_work_change();
        """);
}
