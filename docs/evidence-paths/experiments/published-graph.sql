-- Run with psql -X -qAt -v ON_ERROR_STOP=1 using the published-view role.
-- One result row is the input for project-sponsors.py.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '5s';
SET LOCAL lock_timeout = '500ms';

WITH bills AS MATERIALIZED (
    SELECT record_id, revision_number, bill_number
    FROM public_api.bills
    WHERE congress = 119 AND bill_type = 'hr' AND bill_number IN (2102, 9600)
), sponsorships AS MATERIALIZED (
    SELECT b.bill_number, s.official_id, s.role
    FROM bills b
    JOIN public_api.bill_sponsorships s USING (record_id, revision_number)
    WHERE s.withdrawn_on IS NULL AND s.role IN ('Sponsor', 'Cosponsor')
), shared AS (
    SELECT DISTINCT a.official_id
    FROM sponsorships a
    JOIN sponsorships b USING (official_id)
    WHERE a.bill_number = 2102 AND b.bill_number = 9600
), votes AS (
    SELECT b.bill_number, v.record_id, v.revision_number, v.kind,
           v.roll_call_number, v.voted_on, v.yea, v.nay
    FROM public_api.votes v
    JOIN bills b ON b.record_id = v.bill_record_id
), records AS (
    SELECT kind, count(*) AS total FROM public_api.active_records GROUP BY kind
), documents AS (
    SELECT e.document_number, e.record_id, e.revision_number,
           (SELECT count(*) FROM public_api.executive_action_sources s
            WHERE (s.record_id, s.revision_number) = (e.record_id, e.revision_number)) AS sources,
           (SELECT count(*) FROM public_api.executive_action_summary_sources s
            WHERE (s.record_id, s.revision_number) = (e.record_id, e.revision_number)) AS summary_sources
    FROM public_api.executive_actions e
    WHERE e.document_number IN ('2026-23348', '2026-18738')
)
SELECT json_build_object(
    'release', release_number,
    'as_of_utc', transaction_timestamp() AT TIME ZONE 'UTC',
    'record_counts', (SELECT json_object_agg(kind, total ORDER BY kind) FROM records),
    'bills', ARRAY(SELECT row_to_json(b) FROM bills b ORDER BY bill_number),
    'sponsorships', ARRAY(SELECT row_to_json(s) FROM sponsorships s ORDER BY bill_number, official_id, role),
    'sql_shared', ARRAY(SELECT official_id FROM shared ORDER BY official_id),
    'votes', ARRAY(SELECT row_to_json(v) FROM votes v ORDER BY bill_number, voted_on, record_id),
    'executive_documents', ARRAY(SELECT row_to_json(e) FROM documents e ORDER BY document_number)
)
FROM public_api.active_release;
ROLLBACK;
