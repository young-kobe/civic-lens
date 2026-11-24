-- Export one frozen published release snapshot from public_api views in a single REPEATABLE READ transaction (D-22).
-- Run with a published-view connection (role civic_lens_public) and save output outside Git with SHA256SUMS.
-- Example:
--   psql -X -qAt -v ON_ERROR_STOP=1 --dbname=service=civic_published \
--     -f docs/evidence-paths/experiments/export-frozen-release.sql > /tmp/civic-release-export.json

BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '500ms';

WITH rel AS (
    SELECT release_number, published_at_utc_ticks
    FROM public_api.active_release
),
counts AS (
    SELECT kind, count(*) AS n
    FROM public_api.active_records
    GROUP BY kind
),
bills AS (
    SELECT record_id, revision_number, congress, bill_type, bill_number, bill_title,
           policy_area, policy_area_id, event_date, introduced_on, public_law,
           sponsor_count, cosponsor_count, text_version_name, text_version_date,
           crs_summary_version, summary_from_crs
    FROM public_api.bills
    ORDER BY congress, bill_type, bill_number, record_id
),
votes AS (
    SELECT record_id, revision_number, chamber, congress, session_number, roll_call_number,
           voted_on, kind, bill_type, bill_number, bill_record_id,
           question, result, yea, nay, present, not_voting, alignment
    FROM public_api.votes
    ORDER BY congress, chamber, session_number, roll_call_number, record_id
),
statements AS (
    SELECT s.record_id, s.revision_number, s.statement_date, s.headline, s.summary,
           s.text_origin, s.issue_ids,
           COALESCE((
               SELECT jsonb_agg(sp.official_id ORDER BY sp.position)
               FROM public_api.statement_speakers sp
               WHERE sp.record_id = s.record_id AND sp.revision_number = s.revision_number
           ), '[]'::jsonb) AS speakers
    FROM public_api.statements s
    ORDER BY s.statement_date NULLS LAST, s.record_id
),
executive_actions AS (
    SELECT record_id, revision_number, kind, document_number, signed_on, published_on,
           federal_register_citation, official_id, president_name, headline
    FROM public_api.executive_actions
    ORDER BY signed_on NULLS LAST, document_number, record_id
),
relationships AS (
    SELECT record_id, revision_number, link_type, statement_date, statement_record_id,
           statement_revision_number, target_kind, target_record_id, target_revision_number,
           linked_official_id, cited_vote_cast, cited_vote_date, approver
    FROM public_api.relationships
    ORDER BY record_id
)
SELECT jsonb_build_object(
    'exported_at_utc', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'release', (SELECT jsonb_build_object('release_number', release_number, 'published_at_utc_ticks', published_at_utc_ticks) FROM rel),
    'counts', COALESCE((SELECT jsonb_object_agg(kind, n) FROM counts), '{}'::jsonb),
    'bills', COALESCE((SELECT jsonb_agg(to_jsonb(b)) FROM bills b), '[]'::jsonb),
    'votes', COALESCE((SELECT jsonb_agg(to_jsonb(v)) FROM votes v), '[]'::jsonb),
    'statements', COALESCE((SELECT jsonb_agg(to_jsonb(st)) FROM statements st), '[]'::jsonb),
    'executive_actions', COALESCE((SELECT jsonb_agg(to_jsonb(ea)) FROM executive_actions ea), '[]'::jsonb),
    'relationships', COALESCE((SELECT jsonb_agg(to_jsonb(r)) FROM relationships r), '[]'::jsonb)
)::text;

ROLLBACK;
